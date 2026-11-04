using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShiftManager.Api;
using ShiftManager.Data;
using ShiftManager.Domain;
using ShiftManager.Services.AutomaticScheduling;
namespace ShiftManager.Tests;

public sealed partial class SchedulingTests
{
    [PostgresFact]
    public async Task Cross_month_on_call_exchange_reserves_both_assignments_and_republishes_both_months_atomically()
    {
        clock.Now = DateTimeOffset.Parse("2027-04-01T00:00:00Z");
        var (admin, definition, _) = await PairSetup(); using(admin)
        {
            using(var scope=factory.Services.CreateScope())
            {
                var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var u=(await users.FindByIdAsync(ids["coordinator"].ToString()))!; await users.AddToRoleAsync(u,"EMPLOYEE");
            }
            string Path(int month)=>$"/api/admin/scheduling/periods/2027/{month}";
            async Task<ReviewResponse> Read(int month)=>(await admin.GetFromJsonAsync<ReviewResponse>(Path(month)))!;
            async Task<PublicationResponse> PublishMonth(int month,string type)
            {
                var r=await Read(month);
                if(r.NeedsReview)
                {
                    var ack=await Send(admin,Path(month)+"/review",new ReviewTokenRequest(r.Token),"POST");
                    Assert.Equal(HttpStatusCode.OK,ack.StatusCode);r=(await ack.Content.ReadFromJsonAsync<ReviewResponse>())!;
                }
                var response=await Send(admin,Path(month)+"/publications/"+type,new PublishScheduleRequest(r.Token,true,true,null),"POST");
                Assert.Equal(HttpStatusCode.OK,response.StatusCode);return (await response.Content.ReadFromJsonAsync<PublicationResponse>())!;
            }
            var r=await Read(4);
            Assert.Equal(HttpStatusCode.OK,(await Send(admin,Path(4)+"/on-call-pair",new OnCallPairRequest(r.Token,"ASSIGN",new(2027,4,30),ids["employee"],null,true),"POST")).StatusCode);
            await PublishMonth(4,"PRELIMINARY"); var april=await PublishMonth(4,"FINAL");
            await PublishMonth(5,"PRELIMINARY"); var may=await PublishMonth(5,"FINAL");
            using var employee=await Login("employee");using var replacement=await Login("coordinator");
            var assignment=april.Snapshot!.Cells.SelectMany(c=>c.Employees).Single();
            var create=await Send(employee,ExchangePath+"/2027/4",new ExchangeCreateRequest(april.Id,assignment.AssignmentId,ids["coordinator"]),"POST");
            Assert.Equal(HttpStatusCode.Created,create.StatusCode);var request=(await create.Content.ReadFromJsonAsync<ExchangeResponse>())!;Assert.Equal(2,request.Assignments.Length);
            var other=may.Snapshot!.Cells.SelectMany(c=>c.Employees).Single();
            Assert.Equal(HttpStatusCode.Conflict,(await Send(employee,ExchangePath+"/2027/5",new ExchangeCreateRequest(may.Id,other.AssignmentId,ids["coordinator"]),"POST")).StatusCode);
            request=await ExchangeDecision(replacement,request,"ACCEPT");request=await ExchangeDecision(admin,request,"APPROVE",true);
            var single=await Send(admin,Path(4)+"/publications/FINAL",new PublishScheduleRequest((await Read(4)).Token,true,true,null),"POST");
            Assert.Equal(HttpStatusCode.Conflict,single.StatusCode);Assert.Equal("EXCHANGE_PAIR_PUBLICATION",(await single.Content.ReadFromJsonAsync<ApiError>())!.Code);
            var reviews=(await admin.GetFromJsonAsync<ReviewResponse[]>($"/api/admin/shift-exchanges/{request.Id}/publication-review"))!;
            Assert.Equal(2,reviews.Length);
            var payload=new ExchangePublicationRequest(reviews.Select(x=>new ExchangePeriodToken(x.Draft.Year,x.Draft.Month,x.Token)).ToArray(),true,true,"תיקון זוג כוננות");
            // Failure in the second publication must undo the first publication and its exchange state.
            using(var scope=factory.Services.CreateScope())
            {
                var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await db.Database.ExecuteSqlRawAsync("""
                    CREATE FUNCTION fail_second_publication() RETURNS trigger LANGUAGE plpgsql AS $$
                    BEGIN IF EXISTS (SELECT 1 FROM "SchedulePeriod" WHERE "Id"=NEW."SchedulePeriodId" AND "Month"=5) THEN RAISE EXCEPTION 'forced pair failure'; END IF; RETURN NEW; END $$;
                    CREATE TRIGGER fail_second_publication BEFORE INSERT ON "SchedulePublication" FOR EACH ROW EXECUTE FUNCTION fail_second_publication();
                    """);
            }
            Assert.Equal(HttpStatusCode.InternalServerError,(await Send(admin,$"/api/admin/shift-exchanges/{request.Id}/publish",payload,"POST")).StatusCode);
            Assert.Equal(april.Id,(await employee.GetFromJsonAsync<PublicationResponse>("/api/my-schedule/2027/4"))!.Id);
            Assert.Equal(may.Id,(await employee.GetFromJsonAsync<PublicationResponse>("/api/my-schedule/2027/5"))!.Id);
            using(var scope=factory.Services.CreateScope())await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.ExecuteSqlRawAsync("DROP TRIGGER fail_second_publication ON \"SchedulePublication\"; DROP FUNCTION fail_second_publication()");
            var published=await Send(admin,$"/api/admin/shift-exchanges/{request.Id}/publish",payload,"POST");
            Assert.Equal(HttpStatusCode.OK,published.StatusCode);Assert.Equal(2,(await published.Content.ReadFromJsonAsync<PublicationResponse[]>())!.Length);
            foreach(var month in new[]{4,5})
                Assert.Equal(ids["coordinator"],(await employee.GetFromJsonAsync<PublicationResponse>($"/api/my-schedule/2027/{month}"))!.Snapshot!.Cells.SelectMany(c=>c.Employees).Single().UserId);
            Assert.Equal("APPLIED",(await employee.GetFromJsonAsync<ExchangeResponse[]>(ExchangePath))!.Single().Status);
        }
    }
}
