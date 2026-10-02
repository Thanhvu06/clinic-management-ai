using System.Security.Claims;
using System.Text.Json;
using System.Diagnostics;
using ClinicManagement.Application.AI;
using ClinicManagement.Application.AI.Conversation;
using ClinicManagement.Application.AI.DTOs;
using ClinicManagement.Application.AI.Interfaces;
using ClinicManagement.Application.AI.Planning;
using ClinicManagement.Application.AI.Tools;
using ClinicManagement.Application.Authentication.Interfaces;
using ClinicManagement.Infrastructure.AI;
using ClinicManagement.Infrastructure.AI.Planning;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

var search=new DirectoryInfo(Directory.GetCurrentDirectory());
while(search is not null && !File.Exists(Path.Combine(search.FullName,"src","backend","ClinicManagement.sln"))) search=search.Parent;
var root=search?.FullName ?? throw new DirectoryNotFoundException("Run from the repository.");
var data=root+"/src/tools/ClinicManagement.AI.Training/data/";
var before=args.Contains("before");
var enabled=args.Contains("enabled");
var services=new ServiceCollection();
services.AddLogging();
services.AddSingleton<IVietnameseIntentClassifier>(new VietnameseIntentClassifier());
services.AddScoped<IAiDeterministicPlanner,AiDeterministicPlanner>();
if(!before) {
 var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
 ["RoleIntentModel:Enabled"]=enabled.ToString(),["RoleIntentModel:Directory"]=root+"/src/tools/ClinicManagement.AI.Training/models/role-intent-v2",
 ["RoleIntentModel:LabelsPath"]=data+"role_intent_labels_v1.json" }).Build();
 services.AddRoleIntentRuntime(config);
 services.AddSingleton<IRoleIntentModel>(sp=>new TimingModel(sp.GetRequiredService<RoleIntentModel>()));
}
using var provider=services.BuildServiceProvider();
var labels=JsonDocument.Parse(File.ReadAllText(data+"role_intent_labels_v1.json")).RootElement.EnumerateArray()
 .Where(x=>x.GetProperty("targetTool").ValueKind==JsonValueKind.String).ToDictionary(x=>x.GetProperty("targetTool").GetString()!,x=>x.GetProperty("label").GetString()!);
var output=new List<object>();
async Task<Row> Run(string text,string role,string expected,string id,bool labelExpected=false,double weight=1){
 using var scope=provider.CreateScope();
 var actualRole=Enum.Parse<AiActorRole>(role);
 var timing=provider.GetService<IRoleIntentModel>() as TimingModel;
 var previousMilliseconds=timing?.Milliseconds ?? 0;
 var planner=new TracePlanner(scope.ServiceProvider.GetRequiredService<IAiDeterministicPlanner>());
 var http=new HttpContextAccessor{HttpContext=new DefaultHttpContext{User=new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.Role,role)},"offline"))}};
 var current=new Mock<ICurrentUserService>();current.SetupGet(x=>x.UserId).Returns(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
 var resolver=new Mock<IAiCopilotContextResolver>();resolver.Setup(x=>x.ResolveAsync(It.IsAny<AiCopilotRequestDto>(),It.IsAny<AiConversationMemoryState?>(),It.IsAny<AiActorRole>(),It.IsAny<Guid?>(),It.IsAny<CancellationToken>())).ReturnsAsync(AiContextResolutionResult.Valid(new()));
 var memory=new Mock<IAiConversationMemoryStore>();memory.Setup(x=>x.SaveTurnAsync(It.IsAny<AiConversationMemoryWriteRequest>(),It.IsAny<CancellationToken>())).ReturnsAsync(new AiConversationMemoryState());
 var structured=new Mock<IAiStructuredPlanner>();structured.Setup(x=>x.PlanAsync(It.IsAny<AiStructuredPlannerRequest>(),It.IsAny<CancellationToken>())).ReturnsAsync(new AiStructuredPlannerResult{ProviderState=AiProviderStatusContract.Disabled,Decision=new AiPlannerDecision{Intent=AiChatIntentTypes.ClarificationRequired,PlannerMode=AiPlannerModes.Fallback,Message="Offline provider disabled",Clarification="Offline provider disabled"}});
 var executor=new Mock<IAiToolExecutor>();executor.Setup(x=>x.ExecutePlannerPlanAsync(It.IsAny<IReadOnlyList<AiPlannerToolCall>>(),It.IsAny<string?>(),It.IsAny<CancellationToken>())).ReturnsAsync((IReadOnlyList<AiPlannerToolCall> calls,string? session,CancellationToken ct)=>(IReadOnlyList<AiToolExecutionResult>)calls.Select(c=>new AiToolExecutionResult{Status="completed",ResultType=c.Name,DisplayText="Synthetic read result"}).ToArray());
 var audit=new Mock<IAiAuditService>();audit.Setup(x=>x.LogActionAsync(It.IsAny<AiAuditLogEntry>(),It.IsAny<CancellationToken>())).ReturnsAsync(AiAuditWriteResult.Success());
 var orchestrator=new RoleAwareCopilotOrchestrator(http,current.Object,new AiConversationPipeline(scope.ServiceProvider.GetRequiredService<IVietnameseIntentClassifier>(),new AiSafetyGuard()),planner,structured.Object,resolver.Object,memory.Object,new AiGroundedResponseComposer(),executor.Object,audit.Object);
 var response=await orchestrator.ChatAsync(new AiCopilotRequestDto{Message=text,SessionId="offline-"+id,ConversationId="offline-"+id,ClientTurnId=id});
 var decision=planner.Last;
 var tool=decision?.ToolCalls.FirstOrDefault()?.Name;
 var label=tool is not null && labels.TryGetValue(tool,out var mapped)?mapped:decision?.SubIntent switch {
 "BookingWizard"=>"StartBooking","Greeting"=>"Greeting","RoleHelp"=>"Help","ClinicKnowledge"=>"ClinicKnowledge",
 "WriteRequiresExplicitActionConfirmation" or "MixedReadWritePlan"=>"ActionRequest",_=>decision?.Intent switch {AiChatIntentTypes.StartBooking=>"StartBooking",AiChatIntentTypes.Greeting=>"Greeting",AiChatIntentTypes.Help=>"Help",_=>"Unresolved"}};
 var route=planner.Inner.GetType().GetProperty("LastRoute")?.GetValue(planner.Inner);
 var source=route?.GetType().GetProperty("Source")?.GetValue(route)?.ToString()??(decision is null||decision.RequiresProvider?"fallback":"rule");
 if(response.AssistantMode==AiAssistantModes.SafetyBlocked) source="rule";
 if(source=="model" && decision is not null && response.Intent==decision.Intent) label=((HybridIntentRouter)planner.Inner).LastRoute!.Label;
 return new(id,role,expected,response.Intent,label,source,labelExpected?expected==label:expected==response.Intent,response.ProviderState,weight,(timing?.Milliseconds??0)-previousMilliseconds);
}
var phase=JsonDocument.Parse(File.ReadAllText(data+"phase5_blind_holdout.json")).RootElement.EnumerateArray().ToArray();
var phaseRows=new List<Row>();foreach(var x in phase)phaseRows.Add(await Run(x.GetProperty("inputVi").GetString()!,x.GetProperty("actor").GetString()!,x.GetProperty("expectedIntent").GetString()!,x.GetProperty("caseId").GetString()!));
object Summary(List<Row> rows,bool label)=>new{Count=rows.Select(x=>x.Id).Distinct().Count(),RuntimeTurns=rows.Count,
 Correct=rows.Where(x=>x.Correct).Sum(x=>x.Weight),Accuracy=rows.Where(x=>x.Correct).Sum(x=>x.Weight)/rows.Sum(x=>x.Weight),
 Sources=rows.GroupBy(x=>x.Source).ToDictionary(g=>g.Key,g=>new{Count=g.Sum(x=>x.Weight),Rate=g.Sum(x=>x.Weight)/rows.Sum(x=>x.Weight)}),
 PerLabel=rows.GroupBy(x=>x.Expected).Select(g=>new{Label=g.Key,Count=g.Sum(x=>x.Weight),Correct=g.Where(x=>x.Correct).Sum(x=>x.Weight),Accuracy=g.Where(x=>x.Correct).Sum(x=>x.Weight)/g.Sum(x=>x.Weight)}).ToArray(),Rows=rows};
output.Add(new{Split="phase5",Result=Summary(phaseRows,false)});
if(!before){
 var codes=new Dictionary<string,string>{{"BenhNhan","Patient"},{"LeTan","Receptionist"},{"BacSi","Doctor"},{"KTV","DiagnosticTechnician"},{"DuocSi","Pharmacist"},{"Admin","Admin"}};
 var evalRows=new List<Row>();int i=0;foreach(var line in File.ReadAllLines(data+"role_intent_eval_ai_v1.tsv").Skip(1)) {
  var c=line.Split('\t');var id="eval-"+(++i);
  if(c[0]=="Chung") foreach(var role in Enum.GetValues<AiActorRole>()) evalRows.Add(await Run(c[1],role.ToString(),c[2],id,true,1d/6));
  else evalRows.Add(await Run(c[1],codes.GetValueOrDefault(c[0],c[0]),c[2],id,true));
 }
 output.Add(new{Split="eval",Result=Summary(evalRows,true)});
 var ordered=evalRows.OrderBy(x=>x.ModelMilliseconds).ToArray();var target=.95*evalRows.Sum(x=>x.Weight);double cumulative=0,p95=0;
 foreach(var row in ordered){cumulative+=row.Weight;if(cumulative>=target){p95=row.ModelMilliseconds;break;}}
 output.Add(new{Split="turn-model-latency",RuntimeTurns=evalRows.Count,MeanAddedMilliseconds=evalRows.Sum(x=>x.ModelMilliseconds*x.Weight)/evalRows.Sum(x=>x.Weight),P95AddedMilliseconds=p95,Note="Only time inside the real model call; zero for skipped model. Weighted shared-role turns; serial offline benchmark."});
}
if(!before && enabled){
 var loaded=provider.GetRequiredService<RoleIntentModel>();
 if(!loaded.IsAvailable) throw new InvalidOperationException("Benchmark requires available frozen model.");
 var seeds=ClinicManagement.AI.Training.RoleIntentDatasetGenerator.ReadSeeds(data);
 var times=new List<double>();
 for(int index=-50;index<1000;index++){
  var sample=seeds[(index+50)%seeds.Count];
  var role=sample.Role=="Chung"?AiActorRole.Patient:Enum.Parse<AiActorRole>(ClinicManagement.AI.Training.RoleIntentDatasetGenerator.RoleCodes[sample.Role]);
  var timer=Stopwatch.StartNew(); loaded.Predict(sample.Text,role); timer.Stop(); if(index>=0)times.Add(timer.Elapsed.TotalMilliseconds);
 }
 output.Add(new{Split="latency",Samples=times.Count,Warmups=50,MeanMilliseconds=times.Average(),P95Milliseconds=times.Order().ElementAt((int)Math.Ceiling(.95*times.Count)-1),LoadMilliseconds=loaded.LoadMilliseconds,Note="Serial local model normalization/mask/prediction on frozen seeds; excludes provider, HTTP, database and queueing."});
}
var path=Path.Combine(Path.GetTempPath(),"clinic-role-runtime-"+(before?"before":enabled?"enabled":"disabled")+".json");
File.WriteAllText(path,JsonSerializer.Serialize(output,new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine(path);foreach(var x in output)Console.WriteLine(JsonSerializer.Serialize(x).Split("\"Rows\"")[0]);
record Row(string Id,string Role,string Expected,string Intent,string Label,string Source,bool Correct,string ProviderState,double Weight,double ModelMilliseconds);
sealed class TracePlanner(IAiDeterministicPlanner inner):IAiDeterministicPlanner {
 public IAiDeterministicPlanner Inner=>inner; public AiPlannerDecision? Last{get;private set;}
 public AiPlannerDecision Plan(AiCopilotPlanningContext context)=>Last=inner.Plan(context);
 public AiPlannerDecision PlanSuggestion(ClinicManagement.Application.AI.Suggestions.AiSuggestionDefinition? suggestion,AiResolvedResourceContext resource)=>Last=inner.PlanSuggestion(suggestion,resource);
}
sealed class TimingModel(RoleIntentModel inner):IRoleIntentModel {
 public double Milliseconds{get;private set;}
 public bool IsAvailable=>inner.IsAvailable;
 public double Threshold=>inner.Threshold;
 public bool IsLabelAllowed(string label,AiActorRole role)=>inner.IsLabelAllowed(label,role);
 public RoleIntentModelPrediction? Predict(string text,AiActorRole role){var timer=Stopwatch.StartNew();try{return inner.Predict(text,role);}finally{Milliseconds+=timer.Elapsed.TotalMilliseconds;}}
}
