#!/usr/bin/env python3
"""Offline regression checks for operations that must refuse unsafe targets."""
import copy
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import subprocess
import tarfile
import tempfile
import unittest
from unittest.mock import Mock, patch

from pilot import Pilot, identifier


spec=importlib.util.spec_from_file_location('pilot_deployment',Path(__file__).with_name('test-pilot-deployment.py'))
deployment=importlib.util.module_from_spec(spec)
spec.loader.exec_module(deployment)


class DeploymentDiagnostics(unittest.TestCase):
    def test_failure_exports_fixed_codes_without_credentials_or_exception_payloads(self):
        canary='SyntheticPrivateDiagnosticCanary!'
        failure=deployment.failure_details('initial-migration',RuntimeError(canary),
            [('password authentication failed for '+canary).encode(),('Permission denied: '+canary).encode()])
        self.assertEqual(failure['stage'],'initial-migration')
        self.assertEqual(failure['type'],'RuntimeError')
        self.assertEqual(set(failure['diagnostics']),{'DATABASE_AUTHENTICATION_FAILED','FILESYSTEM_PERMISSION_DENIED'})
        self.assertNotIn(canary,json.dumps(failure))

    def test_unknown_non_utf8_diagnostics_remain_private(self):
        failure=deployment.failure_details('postgres-start',ValueError('private payload'),[b'\xffprivate payload'])
        self.assertEqual(failure['diagnostics'],[])
        self.assertNotIn('private payload',json.dumps(failure))

    def test_configuration_rejection_identifies_only_the_allowlisted_category(self):
        failure=deployment.failure_details('initial-migration',RuntimeError('private'),
            [b'Pilot configuration invalid: key directory permissions.'])
        self.assertIn('KEY_DIRECTORY_PERMISSIONS_INVALID',failure['diagnostics'])

    def test_image_directory_metadata_exposes_insecure_modes_and_owners(self):
        for uid,gid,mode in [(1654,1654,0o700),(1654,1654,0o755),(0,0,0o700)]:
            data=io.BytesIO()
            with tarfile.open(fileobj=data,mode='w') as archive:
                entry=tarfile.TarInfo('keys');entry.type=tarfile.DIRTYPE;entry.uid=uid;entry.gid=gid;entry.mode=mode
                archive.addfile(entry)
            with self.subTest(uid=uid,mode=mode):
                self.assertEqual(deployment.key_directory_metadata(data.getvalue()),{'uid':uid,'gid':gid,'mode':f'{mode:04o}'})

    def test_production_image_must_not_embed_key_material(self):
        data=io.BytesIO()
        with tarfile.open(fileobj=data,mode='w') as archive:
            entry=tarfile.TarInfo('keys');entry.type=tarfile.DIRTYPE;archive.addfile(entry)
            entry=tarfile.TarInfo('keys/key-example.xml');archive.addfile(entry)
        with self.assertRaisesRegex(RuntimeError,'empty key directory'):deployment.key_directory_metadata(data.getvalue())

    def test_insecure_image_is_rejected_before_network_or_database_creation(self):
        data=io.BytesIO()
        with tarfile.open(fileobj=data,mode='w') as archive:
            entry=tarfile.TarInfo('keys');entry.type=tarfile.DIRTYPE;entry.uid=1654;entry.gid=1654;entry.mode=0o755
            archive.addfile(entry)
        validation=deployment.Validation.__new__(deployment.Validation)
        validation.id='a'*32;validation.image='test-image';validation.report={}
        outputs=[json.dumps([{'Id':'test-image-id'}]).encode(),b'b'*64,
                 json.dumps([{'Config':{'Labels':{'shiftflow.pilot.validation':validation.id}}}]).encode(),data.getvalue(),b'']
        validation.command=Mock(side_effect=[subprocess.CompletedProcess([],0,value,b'') for value in outputs])
        with self.assertRaisesRegex(RuntimeError,'mode 0700'):validation.setup()
        self.assertEqual(validation.last_step,'image-key-directory')
        self.assertEqual(validation.report['imageKeyDirectory']['mode'],'0755')
        self.assertEqual(validation.command.call_count,5)
        self.assertEqual(validation.command.call_args.args[0][:3],['docker','rm','-v'])


class OperationGuards(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix='shiftflow-pilot-guards-')
        self.config = {'id': 'a'*32, 'database': 'shiftflow_pilot_'+'a'*32, 'kind': 'disposable',
                       'secrets': self.directory.name, 'environment': {'PILOT_BIND_ADDRESS': '127.0.0.1'}}

    def tearDown(self): self.directory.cleanup()

    def pilot(self, restore=False):
        config=copy.deepcopy(self.config)
        if restore: config['database'] += '_restore_1234ab'
        pilot=Pilot(config);pilot.verify=Mock();pilot.sql=Mock(return_value=subprocess.CompletedProcess([],0,b'',b''))
        return pilot

    def archive(self, **changes):
        archive=Path(self.directory.name)/'synthetic.dump';archive.write_bytes(b'synthetic guard fixture')
        manifest={'deploymentId':self.config['id'],'database':self.config['database'],'encrypted':False,
                  'sha256':hashlib.sha256(archive.read_bytes()).hexdigest()}
        manifest.update(changes)
        archive.with_suffix('.dump.json').write_text(json.dumps(manifest))
        return archive

    def test_identifiers_reject_sql_fragments(self):
        for value in ('postgres;DROP DATABASE x', 'UPPER', '', 'a'*64, 'db"'):
            with self.subTest(value=value), self.assertRaises(ValueError): identifier(value)

    def test_recovery_name_fits_postgres_identifier_limit_without_truncation(self):
        pilot=self.pilot(restore=True)
        self.assertEqual(len(pilot.database.encode()),63)
        identifier(pilot.database)
        with self.assertRaises(ValueError):Pilot({**pilot.config,'database':pilot.database+'d'})

    def test_unknown_environment_and_persistent_names_are_rejected(self):
        for key,value in [('kind','development'),('id','../data'),('database','shiftflow'),('database','postgres')]:
            config={**self.config,key:value}
            with self.subTest(key=key,value=value), self.assertRaises(ValueError):Pilot(config)

    def test_disposable_resources_cannot_bind_externally(self):
        self.config['environment']['PILOT_BIND_ADDRESS']='0.0.0.0'
        with self.assertRaises(ValueError):Pilot(self.config)

    def test_wrong_container_labels_prevent_sql(self):
        pilot=Pilot(self.config)
        pilot.compose=Mock(return_value=subprocess.CompletedProcess([],0,b'b'*64,b''))
        pilot.command=Mock(return_value=subprocess.CompletedProcess([],0,json.dumps([{'Config':{'Labels':{},'Image':'wrong'}}]).encode(),b''))
        with self.assertRaisesRegex(RuntimeError,'ownership'):pilot.sql('SELECT 1')
        self.assertEqual(pilot.command.call_count,1)

    def test_existing_database_is_never_adopted(self):
        pilot=self.pilot();pilot.sql.return_value.stdout=b'1'
        with self.assertRaisesRegex(RuntimeError,'already exists'):pilot.provision()
        self.assertEqual(pilot.sql.call_count,1)

    def test_unencrypted_live_backup_is_refused_before_process_start(self):
        pilot=self.pilot();pilot.config['kind']='pilot'
        with patch('pilot.subprocess.Popen') as start:
            with self.assertRaisesRegex(RuntimeError,'encryption'):pilot.backup(Path(self.directory.name)/'new.dump')
            start.assert_not_called()

    def test_existing_archive_is_never_replaced(self):
        pilot=self.pilot();archive=self.archive()
        with self.assertRaisesRegex(RuntimeError,'must be new'):pilot.backup(archive)
        self.assertEqual(archive.read_bytes(),b'synthetic guard fixture')

    def test_restore_refuses_primary_and_nonempty_databases(self):
        for recovery in (False,True):
            pilot=self.pilot(restore=recovery)
            if recovery:pilot.sql.return_value.stdout=b'1'
            with self.subTest(recovery=recovery), patch('pilot.subprocess.Popen') as start:
                with self.assertRaises(RuntimeError):pilot.restore(self.archive())
                start.assert_not_called()

    def test_restore_refuses_wrong_identity_and_corrupted_archive(self):
        for changes in ({'deploymentId':'b'*32},{'sha256':'0'*64}):
            with self.subTest(changes=changes), patch('pilot.subprocess.Popen') as start:
                with self.assertRaisesRegex(RuntimeError,'checksum'):self.pilot(restore=True).restore(self.archive(**changes))
                start.assert_not_called()

    def test_live_restore_requires_encryption(self):
        pilot=self.pilot(restore=True);pilot.config['kind']='pilot'
        with self.assertRaisesRegex(RuntimeError,'Unencrypted'):pilot.restore(self.archive())

    def test_network_ownership_and_isolation_are_required(self):
        pilot=self.pilot()
        for info in ({'Labels':{},'Internal':True},{'Labels':{'shiftflow.pilot.deployment':pilot.id},'Internal':False}):
            pilot.command=Mock(return_value=subprocess.CompletedProcess([],0,json.dumps([info]).encode(),b''))
            with self.subTest(info=info), self.assertRaises(RuntimeError):pilot.verify_networks()


if __name__=='__main__':unittest.main()
