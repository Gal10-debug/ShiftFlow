#!/usr/bin/env python3
"""Validate the actual Production Compose package on newly owned resources and loopback TLS only."""
import argparse
import base64
import copy
from datetime import datetime, timedelta, timezone
import hashlib
import http.cookiejar
import importlib.util
import io
import ipaddress
import json
import os
from pathlib import Path
import secrets
import shutil
import signal
import socket
import ssl
import subprocess
import sys
import tarfile
import time
import traceback
import urllib.error
import urllib.request
import uuid

from pilot import Pilot, POSTGRES, ROOT, identifier


def failure_details(stage, error, private_logs):
    """Export fixed diagnostic codes, never exception messages or raw log lines."""
    markers = {
        'FILESYSTEM_PERMISSION_DENIED': 'permission denied',
        'FILESYSTEM_OPERATION_NOT_PERMITTED': 'operation not permitted',
        'READ_ONLY_FILESYSTEM': 'read-only file system',
        'DATABASE_AUTHENTICATION_FAILED': 'password authentication failed',
        'PILOT_CONFIGURATION_INVALID': 'pilot configuration invalid',
        'KEY_DIRECTORY_PERMISSIONS_INVALID': 'pilot configuration invalid: key directory permissions.',
        'DATA_PROTECTION_CERTIFICATE_INVALID': 'pilot configuration invalid: data protection certificate.',
        'DATA_PROTECTION_CERTIFICATE_TIME_INVALID': 'pilot configuration invalid: data protection certificate validity.',
        'SECRET_FILE_INVALID': 'pilot configuration invalid: required secret file.',
        'DATABASE_IDENTITY_INVALID': 'pilot configuration invalid: database identity or pool limit.',
        'STATIC_IP_REQUIRES_CONFIGURED_SUBNET': 'user specified ip address is supported only',
        'IMAGE_ARCHITECTURE_UNAVAILABLE': 'no matching manifest',
        'IMAGE_PULL_DENIED': 'pull access denied',
        'REGISTRY_RATE_LIMITED': 'toomanyrequests',
        'DISK_SPACE_EXHAUSTED': 'no space left on device',
        'DOTNET_RUNTIME_UNAVAILABLE': 'you must install or update .net',
        'DATABASE_CONNECTION_FAILED': 'pilot operation failed (npgsqlexception)',
        'POSTGRES_OPERATION_FAILED': 'pilot operation failed (postgresexception)',
    }
    content = b'\n'.join(private_logs).decode(errors='replace').lower()
    return {'stage': stage, 'type': type(error).__name__,
            'message': 'Validation failed; raw diagnostics remain private.',
            'diagnostics': [code for code, marker in markers.items() if marker in content]}


def key_directory_metadata(archive):
    """Inspect only archive metadata; never extract or export key contents."""
    with tarfile.open(fileobj=io.BytesIO(archive)) as stream:
        members=stream.getmembers()
    if len(members)!=1 or not members[0].isdir() or members[0].name.rstrip('/')!='keys':
        raise RuntimeError('Production image must contain an empty key directory')
    directory=members[0]
    return {'uid':directory.uid,'gid':directory.gid,'mode':f'{directory.mode & 0o7777:04o}'}


class Client:
    def __init__(self, base, certificate):
        self.base = base
        self.cookies = http.cookiejar.CookieJar()
        self.opener = urllib.request.build_opener(urllib.request.HTTPSHandler(context=ssl.create_default_context(cafile=certificate)),
                                                 urllib.request.HTTPCookieProcessor(self.cookies))

    def request(self, path, method="GET", body=None, expected=200, csrf=True, headers=None):
        headers = dict(headers or {})
        if method != "GET" and csrf:
            _, token, _ = self.request("/api/auth/csrf")
            headers["X-CSRF-TOKEN"] = token["requestToken"]
        data = None if body is None else json.dumps(body).encode()
        if data is not None: headers["Content-Type"] = "application/json"
        request = urllib.request.Request(self.base + path, data=data, method=method, headers=headers)
        try:
            response = self.opener.open(request, timeout=12)
        except urllib.error.HTTPError as error:
            response = error
        raw = response.read()
        if response.status != expected:
            code = "NON_JSON"
            try: code = json.loads(raw).get("code", "UNKNOWN")
            except ValueError: pass
            raise RuntimeError(f"Unexpected HTTP {response.status}; code {code}")
        try: result = json.loads(raw) if raw else None
        except ValueError: result = raw.decode()
        return response.status, result, response.headers


class Validation:
    def __init__(self, image):
        self.id = uuid.uuid4().hex
        self.private = ROOT / ".pilot-private" / self.id
        self.private.mkdir(parents=True, mode=0o700)
        os.chmod(self.private.parent, 0o700)
        self.secret_dir = self.private / "secrets"
        self.secret_dir.mkdir(mode=0o700)
        self.image = image
        self.networks = []
        self.report = {"deploymentId": self.id, "image": image, "checks": [], "startedUtc": datetime.now(timezone.utc).isoformat(),
                       "data": "synthetic only", "publicExposure": False}
        self.pilot = None
        self.last_step = "initialization"
        self.password = "Pilot!" + secrets.token_hex(24)
        self.results = ROOT / "TestResults/phase7e"
        self.results.mkdir(parents=True, exist_ok=True)

    def command(self, args, data=None, check=True, timeout=180):
        result = subprocess.run(args, input=data, capture_output=True, timeout=timeout, cwd=ROOT)
        if check and result.returncode:
            path=self.private/'command-private.log';path.write_bytes(result.stdout+result.stderr);os.chmod(path,0o600)
            raise RuntimeError("Local validation command failed")
        return result

    def passed(self, name, **details):
        self.last_step = name
        self.report["checks"].append({"name": name, "status": "PASS", **details})
        print("Pilot validation passed: " + name, flush=True)

    def secret(self, name, value):
        path = self.secret_dir / name
        if path.exists(): os.chmod(path, 0o600)
        path.write_text(value)
        # The enclosing host directory is 0700. Read-only bind-mounted secrets must
        # be readable by the distinct non-root service UIDs on Docker Desktop.
        os.chmod(path, 0o444)

    def create_edge_network(self):
        # Ask Docker's allocator for an unused subnet, then create our final
        # network with that subnet explicitly configured for static endpoints.
        # Older engines reject static addresses on auto-configured networks.
        prefix='shiftflow-pilot-'+self.id+'-edge'
        probe=prefix+'-allocation'
        labels=['--label','shiftflow.pilot.validation='+self.id,'--label','shiftflow.pilot.deployment='+self.id]
        self.command(['docker','network','create','--internal',*labels,probe])
        self.networks.append(probe)
        info=json.loads(self.command(['docker','network','inspect',probe]).stdout)[0]
        if info.get('Labels',{}).get('shiftflow.pilot.validation')!=self.id:
            raise RuntimeError('Network allocation ownership mismatch')
        subnet=next(ipaddress.ip_network(c['Subnet']) for c in info['IPAM']['Config']
                    if ipaddress.ip_network(c['Subnet']).version==4)
        self.command(['docker','network','rm',probe]);self.networks.remove(probe)
        self.command(['docker','network','create','--internal','--subnet',str(subnet),*labels,prefix])
        self.networks.append(prefix)
        return subnet

    def setup(self):
        self.report['imageId'] = json.loads(self.command(['docker','image','inspect',self.image]).stdout)[0]['Id']
        self.last_step='image-key-directory'
        probe=self.command(['docker','create','--network','none','--read-only','--cap-drop','ALL',
            '--label','shiftflow.pilot.deployment='+self.id,'--label','shiftflow.pilot.validation='+self.id,self.image]).stdout.decode().strip()
        info=json.loads(self.command(['docker','inspect',probe]).stdout)[0]
        if info['Config']['Labels'].get('shiftflow.pilot.validation')!=self.id:raise RuntimeError('Image probe ownership mismatch')
        try:
            metadata=key_directory_metadata(self.command(['docker','cp',probe+':/var/lib/shiftflow/keys','-']).stdout)
            self.report['imageKeyDirectory']=metadata
            if metadata!={'uid':1654,'gid':1654,'mode':'0700'}:
                raise RuntimeError('Production image key directory must be owned by UID/GID 1654 with mode 0700')
        finally:self.command(['docker','rm','-v',probe])
        self.last_step='validation-configuration'
        subnet=self.create_edge_network()
        self.report['explicitPrivateEdgeSubnet']=True
        for purpose in ["data", "ingress"]:
            name = "shiftflow-pilot-" + self.id + "-" + purpose
            self.command(["docker", "network", "create", *([] if purpose == "ingress" else ["--internal"]), "--label", "shiftflow.pilot.validation=" + self.id,
                          "--label", "shiftflow.pilot.deployment=" + self.id, name])
            self.networks.append(name)
        with socket.socket() as s: s.bind(("127.0.0.1", 0)); port = s.getsockname()[1]
        database = "shiftflow_pilot_" + self.id
        environment = {"PILOT_APP_IMAGE": self.image, "PILOT_PROXY_IMAGE": "shiftflow-pilot-proxy:phase7e-local", "PILOT_BIND_ADDRESS": "127.0.0.1", "PILOT_HTTPS_PORT": str(port),
            "PILOT_PUBLIC_ORIGIN": f"https://127.0.0.1:{port}", "PILOT_PUBLIC_HOST": "127.0.0.1",
            "PILOT_PROXY_ADDRESS": str(subnet.network_address + 2), "PILOT_APP_ADDRESS": str(subnet.network_address + 3),
            "PILOT_EDGE_NETWORK": self.networks[0], "PILOT_DATA_NETWORK": self.networks[1], "PILOT_INGRESS_NETWORK": self.networks[2], "PILOT_VALIDATION_RUN": self.id,
            "PILOT_BOOTSTRAP_USERNAME": "pilot-admin", "PILOT_BOOTSTRAP_FIRST_NAME": "מנהל", "PILOT_BOOTSTRAP_LAST_NAME": "בדיקה"}
        self.config = {"id": self.id, "kind": "disposable", "database": database, "secrets": str(self.secret_dir), "environment": environment}
        self.secret("postgres-owner-password", secrets.token_hex(32))
        self.secret("bootstrap-password", self.password)
        self.secret("keys-password", secrets.token_hex(32))
        for purpose in ["runtime", "migrator", "backup"]: self.secret(purpose + "-password", secrets.token_hex(32))
        self.connections(database)
        for purpose, subject in [("tls", "127.0.0.1"), ("keys", "ShiftFlow disposable key protection")]:
            args = ["openssl", "req", "-x509", "-newkey", "rsa:2048", "-nodes", "-days", "2", "-subj", "/CN=" + subject,
                    "-keyout", str(self.secret_dir / (purpose + ".key")), "-out", str(self.secret_dir / (purpose + ".crt"))]
            if purpose == "tls": args += ["-addext", "subjectAltName=IP:127.0.0.1,DNS:localhost"]
            self.command(args)
        self.command(["openssl", "pkcs12", "-export", "-inkey", str(self.secret_dir / "keys.key"), "-in", str(self.secret_dir / "keys.crt"),
                      "-out", str(self.secret_dir / "keys.pfx"), "-passout", "file:" + str(self.secret_dir / "keys-password")])
        for path in self.secret_dir.iterdir(): os.chmod(path, 0o444)
        self.pilot = Pilot(self.config)
        self.pilot.compose("config", "--quiet")
        self.passed("compose-configuration")
        self.last_step = 'postgres-start'
        self.pilot.compose("up", "-d", "postgres")
        self.last_step = 'postgres-readiness'
        self.wait_database()
        self.last_step = 'database-provision'
        self.pilot.provision()
        self.last_step = 'initial-migration'
        self.pilot.compose("run", "--rm", "-T", "--no-deps", "migrate")
        self.last_step = 'migration-history-permissions'
        self.pilot.harden_history()
        self.last_step = 'repeated-migration'
        self.pilot.compose("run", "--rm", "-T", "--no-deps", "migrate")
        self.last_step = 'initial-bootstrap'
        self.pilot.compose("run", "--rm", "-T", "--no-deps", "bootstrap")
        self.last_step = 'repeated-bootstrap'
        self.pilot.compose("run", "--rm", "-T", "--no-deps", "bootstrap")
        self.passed("clean-install-migrations-and-idempotent-bootstrap")
        self.last_step = 'app-and-proxy-start'
        self.pilot.compose("up", "-d", "app", "proxy")
        self.base = environment["PILOT_PUBLIC_ORIGIN"]
        self.admin = Client(self.base, str(self.secret_dir / "tls.crt"))
        self.wait_api(self.admin)
        self.passed("production-https-topology")

    def connections(self, database):
        for purpose in ["runtime", "migrator"]:
            password = (self.secret_dir / (purpose + "-password")).read_text().strip()
            self.secret(purpose.replace("migrator", "migration") + "-connection", f"Host=postgres;Port=5432;Database={database};Username=sf_{self.id}_{purpose};Password={password};Maximum Pool Size=20;Timeout=5;Command Timeout=30;SSL Mode=Disable")
        password = (self.secret_dir / "backup-password").read_text().strip()
        self.secret("backup-pgpass", f"postgres:5432:*:sf_{self.id}_backup:{password}\n")

    def wait_database(self):
        deadline = time.monotonic() + 60
        while time.monotonic() < deadline:
            result = self.pilot.command(["docker", "exec", self.pilot.container("postgres"), "pg_isready", "-h", "127.0.0.1", "-U", "pilot_owner", "-d", "postgres"], check=False)
            if result.returncode == 0: return
            time.sleep(.25)
        raise RuntimeError("Owned database readiness timed out")

    def wait_api(self, client):
        deadline = time.monotonic() + 60
        last = "not attempted"
        while time.monotonic() < deadline:
            try: client.request("/api/auth/csrf"); return
            except (OSError, RuntimeError) as error: last = str(error); time.sleep(.3)
        (self.private/'readiness-private.log').write_text(last)
        raise RuntimeError("Owned API readiness timed out")

    def call(self, client, path, method="GET", body=None, expected=200, **kwargs):
        self.last_step = method + " " + path
        return client.request("/api" + path, method, body, expected, **kwargs)[1]

    def login(self, username, password):
        client = Client(self.base, str(self.secret_dir / "tls.crt"))
        self.call(client, "/auth/login", "POST", {"username": username, "password": password})
        return client

    def change_password(self, client):
        changed = "Changed!" + secrets.token_hex(24)
        self.call(client, "/auth/change-password", "POST", {"currentPassword": self.password, "newPassword": changed})

    def workflow(self):
        self.call(self.admin, "/auth/login", "POST", {"username": "pilot-admin", "password": self.password})
        self.call(self.admin, "/users", expected=403)
        self.change_password(self.admin)
        self.users, self.clients = {}, {}
        for name, role, qualified in [("leader", "EMPLOYEE", True), ("deputy", "EMPLOYEE", False), ("coordinator", "COORDINATOR", False),
                                      ("worker-a", "EMPLOYEE", True), ("worker-b", "EMPLOYEE", True), ("worker-c", "EMPLOYEE", True)]:
            user = self.call(self.admin, "/users", "POST", {"firstName": "בדיקת", "lastName": "משתמש", "username": "pilot-" + name,
                "initialPassword": self.password, "roles": [role], "isShiftLeadQualified": qualified}, 201)
            client = self.login("pilot-" + name, self.password); self.change_password(client)
            self.users[name] = user; self.clients[name] = client
            self.call(client, "/users", expected=403)
            self.call(client, "/admin/scheduling/board", expected=403)
        teams = self.call(self.admin, "/admin/teams")
        for name in ["leader", "deputy", "worker-a", "worker-b", "worker-c"]:
            self.call(self.admin, f"/admin/teams/members/{self.users[name]['id']}", "PUT", {"teamId": teams[0]["id"]})
        teams = self.call(self.admin, "/admin/teams")
        self.call(self.admin, "/admin/teams/" + teams[0]["id"], "PUT", {"name": "צוות בדיקה", "leaderUserId": self.users['leader']['id'],
            "deputyUserId": self.users['deputy']['id'], "expectedVersion": teams[0]['version']})
        now = datetime.now(timezone.utc); first = (now.replace(day=1) + timedelta(days=32)).replace(day=1)
        self.year, self.month = first.year, first.month
        self.period = f"/admin/scheduling/periods/{self.year}/{self.month}"
        self.availability = f"/availability/{self.year}/{self.month}"
        self.call(self.admin, f"/admin/availability/{self.year}/{self.month}", "PUT", {"opensAt": (now-timedelta(hours=1)).isoformat(), "closesAt": (now+timedelta(days=2)).isoformat()})
        self.call(self.admin, f"/admin/availability/{self.year}/{self.month}/state", "PUT", {"isOpen": True})
        for name in ["deputy", "worker-a", "worker-b", "worker-c"]:
            self.call(self.clients[name], self.availability, "POST", {"expectedVersion": 0, "selections": []}, 201)
        day = first
        while day.weekday() in [4,5]: day += timedelta(days=1)
        self.call(self.clients['deputy'], f"/team-availability/employees/{self.users['worker-c']['id']}/{self.year}/{self.month}", "PUT",
                  {"expectedVersion": 1, "selections": [{"date": day.date().isoformat(), "shiftType": "DAY", "state": "BLOCKED"}]})
        self.call(self.admin, f"/admin/availability/{self.year}/{self.month}/state", "PUT", {"isOpen": False})
        rows = self.call(self.admin, "/admin/scheduling/shift-types")
        definitions = [{"id": r['id'], "expectedVersion": r['version'], "name": r['name'], "displayOrder": r['displayOrder'], "period": r['period'],
            "timeRange": r['timeRange'], "requiredEmployees": 4 if r['slot']==26 else 0, "includeInAutomaticScheduling": r['slot']==26,
            "requiresShiftLeadQualification": r['slot']==26, "schedulingPriority": "Critical" if r['slot']==26 else r['schedulingPriority']} for r in rows]
        self.call(self.admin, "/admin/scheduling/shift-types", "PUT", {"shiftTypes": definitions})
        start = time.monotonic()
        job = self.call(self.admin, "/admin/scheduling/generation", "POST", {"year": self.year, "month": self.month}, 202)
        while job['state'] in ['QUEUED','RUNNING'] and time.monotonic()-start < 45:
            time.sleep(.1); job = self.call(self.admin, "/admin/scheduling/generation/" + job['id'])
        assert job['state']=='READY' and job['summary']['missing']>0
        self.call(self.admin, "/admin/scheduling/generation/" + job['id'] + "/shortages")
        self.call(self.admin, "/admin/scheduling/generation/" + job['id'] + "/save", "POST", {})
        self.publish("PRELIMINARY")
        publications = self.call(self.admin, self.period + "/publications")
        original = publications[0]
        visible = self.call(self.clients['worker-a'], f"/my-schedule/{self.year}/{self.month}")
        a = next(e for c in visible['snapshot']['cells'] for e in c['employees'] if e['userId']==self.users['worker-a']['id'])
        b = next(e for c in visible['snapshot']['cells'] for e in c['employees'] if e['userId']==self.users['worker-b']['id'])
        issue = self.call(self.clients['worker-a'], f"/my-schedule/{self.year}/{self.month}/issues", "POST", {"publicationId": visible['id'],
            "assignmentId": a['assignmentId'], "explanation": "בדיקת תהליך תיקון"}, 201)
        review = self.call(self.admin, self.period)
        review = self.call(self.admin, self.period + "/correction", "POST", {"expectedToken": review['token']})
        self.call(self.admin, self.period + "/assignments", "POST", {"expectedToken": review['token'], "operation": "NOTE", "assignmentId": b['assignmentId'], "note": "שיבוץ מוגן לבדיקה"})
        self.call(self.admin, self.period + "/issues/" + issue['id'], "PUT", {"expectedVersion": issue['version'], "status": "RESOLVED", "response": "נבדק"})
        self.publish("FINAL")
        history = self.call(self.admin, self.period + "/publications")
        assert history[-1]['snapshot']==original['snapshot']
        self.passed("provisioning-availability-delegation-generation-shortage-publication-issue-correction", solveSeconds=time.monotonic()-start,
                    solverStatus=job['solverStatus'], missing=job['summary']['missing'])
        reserve = self.call(self.admin, "/users", "POST", {"firstName":"מחליף", "lastName":"בדיקה", "username":"pilot-reserve", "initialPassword": self.password,
            "roles":["EMPLOYEE"], "isShiftLeadQualified":True}, 201)
        self.call(self.admin, f"/admin/teams/members/{reserve['id']}", "PUT", {"teamId": teams[0]['id']})
        recipient = self.login("pilot-reserve",self.password); self.change_password(recipient)
        assignments = self.call(self.clients['worker-a'], f"/shift-exchanges/{self.year}/{self.month}/assignments")
        source = next(x for x in assignments if x['assignmentId']==a['assignmentId'])
        exchange = self.call(self.clients['worker-a'], f"/shift-exchanges/{self.year}/{self.month}", "POST", {
            "publicationId": source['publicationId'], "assignmentId":source['assignmentId'], "replacementUserId":reserve['id']}, 201)
        exchange = self.call(recipient, "/shift-exchanges/"+exchange['id']+"/decision", "POST", {"expectedVersion":exchange['version'],"decision":"ACCEPT"})
        self.call(self.admin, "/admin/shift-exchanges/"+exchange['id']+"/decision", "POST", {"expectedVersion":exchange['version'],"decision":"APPROVE"})
        review=self.reviewed()
        self.call(self.admin, "/admin/shift-exchanges/"+exchange['id']+"/publish", "POST", {"periods":[{"year":self.year,"month":self.month,"expectedToken":review['token']}], "acknowledgeShortages":True,"acknowledgeOpenIssues":True})
        absence=self.call(recipient,"/absences","POST",{"reason":"VACATION","startDate":source['date'],"endDate":source['date'],"recurring":False,"weekdays":[],"fullDay":True,"startTime":None,"endTime":None,"explanation":"בדיקת אישור עצמאי"},201)
        self.call(recipient,"/absences/"+absence['id']+"/decision","POST",{"expectedVersion":absence['version'],"decision":"APPROVED"},403)
        conflicts=self.call(self.clients['deputy'],"/absences/"+absence['id']+"/conflicts")
        self.call(self.clients['deputy'],"/absences/"+absence['id']+"/decision","POST",{"expectedVersion":absence['version'],"decision":"APPROVED","conflictToken":conflicts['token']})
        review=self.call(self.admin,self.period)
        if review['state']!='UNDER_CORRECTION':review=self.call(self.admin,self.period+"/correction","POST",{"expectedToken":review['token']})
        replaced=next(e for c in review['draft']['cells'] if c['date']==source['date'] for e in c['employees'] if e['userId']==reserve['id'])
        self.call(self.admin,self.period+"/assignments","POST",{"expectedToken":review['token'],"operation":"REPLACE","assignmentId":replaced['assignmentId'],"userId":self.users['worker-a']['id'],"definitionId":source['definitionId'],"date":source['date']})
        self.publish("FINAL")
        assert self.call(self.admin,self.period)['conflicts']==[]
        assert self.call(self.admin,self.period+"/publications")[-1]['snapshot']==original['snapshot']
        self.passed("exchange-independent-absence-approval-and-republication")

    def reviewed(self):
        review=self.call(self.admin,self.period)
        if review['needsReview']:review=self.call(self.admin,self.period+"/review","POST",{"expectedToken":review['token']})
        return review

    def publish(self,kind):
        review=self.reviewed()
        return self.call(self.admin,self.period+"/publications/"+kind,"POST",{"expectedToken":review['token'],"acknowledgeShortages":True,"acknowledgeOpenIssues":True,"notes":"בדיקת פיילוט סינתטי"})

    def security_and_recovery(self):
        self.last_step='deployment-security'
        _,_,headers=self.admin.request('/api/auth/csrf')
        assert headers.get('X-Request-ID') and headers.get('Strict-Transport-Security')
        assert 'no-store' in headers.get('Cache-Control','').lower()
        cookies=list(self.admin.cookies)
        auth=next(c for c in cookies if c.name=='__Host-ShiftFlow.Auth')
        attributes={k.lower():str(v).lower() for k,v in auth._rest.items()}
        assert auth.secure and auth.path=='/' and not auth.domain_specified and 'httponly' in attributes
        assert attributes.get('samesite')=='strict'
        assert auth.expires is None and auth.discard  # Session cookie; server ticket has a fixed eight-hour lifetime.
        self.admin.request('/api/users','POST',{},expected=400,csrf=False)
        _,_,cors=self.admin.request('/api/users','OPTIONS',expected=404,csrf=False,headers={'Origin':'https://untrusted.invalid','Access-Control-Request-Method':'POST'})
        assert not cors.get('Access-Control-Allow-Origin') and not cors.get('Access-Control-Allow-Credentials')
        self.admin.request('/health/ready',expected=404)
        self.admin.request('/api/__e2e/ready',expected=404)
        # The proxy overwrites a caller-supplied scheme rather than appending it.
        self.admin.request('/api/auth/csrf',headers={'X-Forwarded-Proto':'http','X-Forwarded-For':'198.51.100.7'})
        direct=self.command(['docker','run','--rm','--network',self.networks[1],'--read-only','--user','1000',
            '--cap-drop','ALL','--memory=128m','--cpus=1','--label','shiftflow.pilot.validation='+self.id,
            'node:24-bookworm-slim@sha256:01d566e8d94d8c619591d78652babfccd3c6992a3dffa9ed5ab39e11cf6c70e9',
            'node','-e',"require('http').get({hostname:'app',port:8080,path:'/api/auth/csrf',headers:{Host:'127.0.0.1','X-Forwarded-Proto':'https','X-Forwarded-For':'198.51.100.7'}},r=>{console.log(r.statusCode,!!r.headers['set-cookie']);r.resume();if(r.statusCode!==307||r.headers['set-cookie'])process.exitCode=1})"])
        assert direct.returncode==0
        try:
            urllib.request.urlopen(self.base+'/api/auth/csrf',context=ssl.create_default_context(),timeout=5)
        except urllib.error.URLError:pass
        else:raise RuntimeError('An untrusted local certificate unexpectedly passed verification')
        app=self.pilot.container('app');database=self.pilot.container('postgres')
        for service in ['app','proxy','postgres']:
            info=json.loads(self.command(['docker','inspect',self.pilot.container(service)]).stdout)[0]
            assert info['Config']['User']!='0' and info['HostConfig']['ReadonlyRootfs']
            assert 'ALL' in info['HostConfig']['CapDrop']
            if service!='proxy': assert not info['HostConfig']['PortBindings']
        for purpose in ['runtime','backup']:
            role=self.pilot.role(purpose)
            permission=self.pilot.sql(f"SELECT rolsuper OR rolcreatedb OR rolcreaterole OR rolreplication FROM pg_roles WHERE rolname='{role}'",self.pilot.database).stdout.strip()
            assert permission==b'f'
            ddl=self.pilot.sql('SET ROLE '+identifier(role)+'; CREATE TABLE public.forbidden_pilot_ddl(id integer)',self.pilot.database,check=False)
            assert ddl.returncode!=0
        history=self.pilot.sql('SET ROLE '+identifier(self.pilot.role('runtime'))+'; UPDATE "__EFMigrationsHistory" SET "ProductVersion"=\'invalid\' WHERE false',self.pilot.database,check=False)
        assert history.returncode!=0
        metadata=self.pilot.compose('run','--rm','-T','--no-deps','maintain').stdout.decode()
        key_info=next(json.loads(line) for line in metadata.splitlines() if line.startswith('{') and 'keyFiles' in line)
        assert key_info['encrypted'] and key_info['keyFiles']>0
        self.report['dataProtection']=key_info
        self.passed('secure-cookies-csrf-cors-host-health-and-least-privilege')
        before=self.pilot.fingerprints()
        self.pilot.compose('restart','app');self.wait_api(self.admin);self.call(self.admin,'/auth/me')
        assert self.pilot.fingerprints()==before
        self.passed('application-restart-keeps-session-and-data')
        self.pilot.compose('stop','-t','35','app')
        stopped=json.loads(self.command(['docker','inspect',app]).stdout)[0]
        assert stopped['State']['ExitCode']==0
        self.pilot.compose('up','-d','--force-recreate','--no-deps','app')
        self.wait_api(self.admin);self.call(self.admin,'/auth/me')
        assert self.pilot.container('app')!=app and self.pilot.fingerprints()==before
        self.passed('graceful-shutdown-container-replacement-and-key-continuity')
        self.pilot.compose('restart','postgres');self.wait_database();self.wait_api(self.admin)
        self.call(self.admin,'/auth/me');assert self.pilot.fingerprints()==before
        self.passed('database-restart-keeps-volume-data')
        app=self.pilot.container('app')
        paused=False
        try:
            self.command(['docker','pause',database]);paused=True
            assert self.command(['docker','exec',app,'dotnet','ShiftManager.dll','--healthcheck','live'],check=False).returncode==0
            assert self.command(['docker','exec',app,'dotnet','ShiftManager.dll','--healthcheck','ready'],check=False).returncode!=0
            self.admin.request('/api/auth/me',expected=503)
        finally:
            if paused:self.command(['docker','unpause',database])
        self.wait_api(self.admin);self.call(self.admin,'/auth/me');assert self.pilot.fingerprints()==before
        self.passed('database-outage-readiness-and-recovery')
        connection=self.secret_dir/'runtime-connection'
        original=connection.read_text()
        try:
            password=(self.secret_dir/'runtime-password').read_text().strip()
            self.secret('runtime-connection',original.replace(password,secrets.token_hex(32)))
            self.pilot.compose('up','-d','--force-recreate','--no-deps','app')
            app=self.pilot.container('app')
            deadline=time.monotonic()+45
            while time.monotonic()<deadline:
                if self.command(['docker','exec',app,'dotnet','ShiftManager.dll','--healthcheck','live'],check=False).returncode==0:break
                time.sleep(.25)
            else:raise RuntimeError('Invalid-credential app liveness timed out')
            assert self.command(['docker','exec',app,'dotnet','ShiftManager.dll','--healthcheck','ready'],check=False).returncode!=0
            self.admin.request('/api/auth/me',expected=503)
        finally:
            self.secret('runtime-connection',original)
            self.pilot.compose('up','-d','--force-recreate','--no-deps','app')
        self.wait_api(self.admin);self.call(self.admin,'/auth/me')
        self.passed('invalid-database-credentials-fail-readiness-and-recover')
        missing=self.command(['docker','run','--rm','--network','none','--read-only','--cap-drop','ALL','--memory=128m','--cpus=1',
            '--label','shiftflow.pilot.validation='+self.id,'-e','PILOT_MODE=1',self.image],check=False)
        assert missing.returncode!=0 and b'Pilot configuration invalid' in missing.stderr+missing.stdout
        self.passed('missing-configuration-fails-closed')
        self.browser()
        backup=self.private/'synthetic-backup.dump'
        expected=self.pilot.fingerprints()
        backup_result=self.pilot.backup(backup)
        replay=Client(self.base,str(self.secret_dir/'tls.crt'))
        for cookie in self.admin.cookies:replay.cookies.set_cookie(copy.copy(cookie))
        self.call(self.admin,'/auth/logout','POST',expected=204)
        replay.request('/api/auth/me',expected=401)
        self.passed('logout-revokes-copied-cookie')
        recovery_start=time.monotonic()
        recovery_config=copy.deepcopy(self.config)
        recovery_config['database']='shiftflow_pilot_'+self.id+'_restore_'+secrets.token_hex(3)
        recovery=Pilot(recovery_config);recovery.provision(restore=True)
        restore_result=recovery.restore(backup)
        assert recovery.fingerprints()==expected
        self.report['backupRestore']={**restore_result,'backupSeconds':backup_result['elapsedSeconds'],
            'databaseTablesVerified':len(expected),'sameDataBeforeSessionInvalidation':True,'rpoTargetApproved':False,'rtoTargetApproved':False,
            'encryption':'private synthetic drill only; live backup requires approved age recipient'}
        # A restored Identity snapshot may resurrect old security stamps. Never expose
        # it before invalidating sessions, even when old keys are still available.
        self.pilot.compose('stop','-t','35','app')
        self.connections(recovery.database)
        recovery.compose('run','--rm','-T','--no-deps','maintain','--pilot-command','invalidate-sessions','--confirm-target',recovery.confirmation)
        recovery.compose('up','-d','--force-recreate','--no-deps','app')
        self.wait_api(replay);replay.request('/api/auth/me',expected=401)
        recovered=recovery.fingerprints()
        assert all(recovered[t]==expected[t] for t in expected if t!='AspNetUsers')
        self.report['backupRestore']['recoveryToReadySeconds']=time.monotonic()-recovery_start
        self.passed('separate-database-backup-restore-and-session-invalidation')

    def browser(self):
        public=self.command(['openssl','x509','-in',str(self.secret_dir/'tls.crt'),'-pubkey','-noout']).stdout
        der=self.command(['openssl','pkey','-pubin','-outform','DER'],data=public).stdout
        config={'base':self.base,'spki':base64.b64encode(hashlib.sha256(der).digest()).decode(),
            'period':f'{self.year}-{self.month:02d}','cookies':[{'name':c.name,'value':c.value,'url':self.base,
                'secure':True,'httpOnly':True,'sameSite':'Strict'} for c in self.admin.cookies]}
        path=self.private/'browser-private.json';path.write_text(json.dumps(config));os.chmod(path,0o600)
        self.command(['node','frontend/e2e/pilot-smoke.mjs',str(path)])
        self.passed('production-browser-hebrew-rtl-and-secure-context')

    def cleanup(self):
        # A probe can exist before Compose/Pilot has been initialized.
        if self.pilot is not None or self.last_step=='image-key-directory':
            ids=self.command(['docker','ps','-aq','--filter','label=shiftflow.pilot.deployment='+self.id],check=False).stdout.decode().split()
            for cid in ids:
                info=json.loads(self.command(['docker','inspect',cid]).stdout)[0]
                if info['Config']['Labels'].get('shiftflow.pilot.validation')!=self.id:raise RuntimeError('Cleanup ownership mismatch')
                self.command(['docker','rm','-f','-v',cid])
            volumes=self.command(['docker','volume','ls','-q','--filter','label=shiftflow.pilot.deployment='+self.id]).stdout.decode().split()
            for volume in volumes:
                info=json.loads(self.command(['docker','volume','inspect',volume]).stdout)[0]
                if info['Labels'].get('shiftflow.pilot.validation')!=self.id:raise RuntimeError('Volume ownership mismatch')
                self.command(['docker','volume','rm',volume])
        for network in self.networks:
            info=json.loads(self.command(['docker','network','inspect',network]).stdout)[0]
            if info['Labels'].get('shiftflow.pilot.validation')!=self.id:raise RuntimeError('Network ownership mismatch')
            self.command(['docker','network','rm',network])


def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--image',default='shiftflow-pilot:phase7e-local');parser.add_argument('--keep-private',action='store_true');args=parser.parse_args()
    validation=Validation(args.image);code=1
    def interrupted(*_): raise InterruptedError()
    signal.signal(signal.SIGTERM,interrupted);signal.signal(signal.SIGINT,interrupted)
    try:
        validation.setup();validation.workflow();validation.security_and_recovery();code=0
    except Exception as error:
        (validation.private/'failure-private.log').write_text(traceback.format_exc())
        if validation.pilot is not None:
            for service in ['app','proxy','postgres']:
                try:
                    cid=validation.pilot.container(service)
                    logs=validation.command(['docker','logs',cid],check=False)
                    (validation.private/(service+'-private.log')).write_bytes(logs.stdout+logs.stderr)
                except Exception:pass
        private_logs=[p.read_bytes() for p in validation.private.glob('*-private.log')]
        validation.report['failure']=failure_details(validation.last_step,error,private_logs)
        failure=validation.report['failure']
        print(f"Pilot validation failed at {failure['stage']} ({failure['type']}); no readiness success is claimed.",flush=True)
        print('Safe diagnostic codes: '+(', '.join(failure['diagnostics']) or 'none'),flush=True)
    finally:
        try:validation.cleanup();validation.report['cleanup']='PASS'
        except Exception:
            validation.report['cleanup']='FAILED';code=1
            print('Pilot resource cleanup failed; no readiness success is claimed.',flush=True)
        validation.report['exitCode']=code
        (validation.results/'deployment-validation.json').write_text(json.dumps(validation.report,indent=2)+'\n')
        if not args.keep_private:shutil.rmtree(validation.private)
    return code


if __name__=='__main__':raise SystemExit(main())
