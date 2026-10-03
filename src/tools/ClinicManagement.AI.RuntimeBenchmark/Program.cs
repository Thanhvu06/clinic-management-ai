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
if(args.Contains("calibrate") || args.Contains("measure-calibration")){
 await RuntimeCalibrationBenchmark.RunAsync(root,args);
 return;
}
var data=root+"/src/tools/ClinicManagement.AI.Training/data/";
var before=args.Contains("before");
var enabled=args.Contains("enabled");
var diagnostics=args.Contains("diagnostics");
if(diagnostics && (!enabled || before)) throw new ArgumentException("Diagnostics require the enabled, frozen hybrid.");
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
var catalogLabels=JsonDocument.Parse(File.ReadAllText(data+"role_intent_labels_v1.json")).RootElement.EnumerateArray()
 .Select(x=>x.GetProperty("label").GetString()!).ToHashSet(StringComparer.Ordinal);
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
 var pipeline=new TracePipeline(new AiConversationPipeline(scope.ServiceProvider.GetRequiredService<IVietnameseIntentClassifier>(),new AiSafetyGuard()));
 var orchestrator=new RoleAwareCopilotOrchestrator(http,current.Object,pipeline,planner,structured.Object,resolver.Object,memory.Object,new AiGroundedResponseComposer(),executor.Object,audit.Object);
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
 var addedMilliseconds=(timing?.Milliseconds??0)-previousMilliseconds;
 var correct=labelExpected?expected==label:expected==response.Intent;
 Diagnostic? detail=null;
 if(diagnostics && labelExpected){
  var rawLabel=route?.GetType().GetProperty("Label")?.GetValue(route)?.ToString();
  var routeScore=route?.GetType().GetProperty("Score")?.GetValue(route) as double?;
  var selectedLabel=source=="fallback"?null:source=="model"?rawLabel:label=="Unresolved"?rawLabel:label;
  string? probeLabel=null;
  var resourceMissing=decision?.SubIntent is "MissingAssignedCase" or "MissingPrescriptionForPayment" or "MissingAppointmentCode" || response.ErrorCode==AiPlannerErrorCodes.ResourceContextRequired;
  // A separate pure-planning probe identifies the existing read target before a
  // missing-resource clarification. Sentinel IDs never reach the orchestrator,
  // resolver, binding pipeline or executor. Neither expected labels nor model
  // predictions participate in this label mapping.
  if(source=="rule" && resourceMissing && planner.Context is {} context){
   var sentinel=new AiResolvedResourceContext{VisitId=long.MaxValue,PrescriptionId=long.MaxValue};
   var probe=new AiDeterministicPlanner().Plan(new AiCopilotPlanningContext{
    Role=context.Role,NormalizedMessage=context.NormalizedMessage,Analysis=context.Analysis,
    Resource=sentinel,CurrentTurnResource=sentinel,Memory=context.Memory});
   var probeTool=probe.ToolCalls.FirstOrDefault()?.Name;
   if(probeTool is not null && labels.TryGetValue(probeTool,out var target)) probeLabel=target;
   else if(decision?.SubIntent=="MissingAppointmentCode") probeLabel="LookupAppointment";
   else if(catalogLabels.Contains(probe.Intent)) probeLabel=probe.Intent;
   if(probeLabel is not null) selectedLabel=probeLabel;
  }
  if(selectedLabel is not null && !catalogLabels.Contains(selectedLabel)) selectedLabel="Unresolved";
  var blocked=resourceMissing?"MissingFixtureResource":decision?.RequiresProvider==true && response.ProviderState==AiProviderStatusContract.Disabled?"ProviderDisabled":null;
  var labelCorrect=selectedLabel==expected;
  string? group=null;
  if(!correct){
   group=source switch{
    "rule"=>labelCorrect?"b":"a", "model"=>labelCorrect?"d":"c",
    "fallback"=>expected is "ActionRequest" or "OutOfScope"?"f":"e",
    _=>throw new InvalidOperationException("Unknown decision source.")};
   if(group is "b" or "d" && blocked is null) throw new InvalidOperationException($"Unexplained label-correct result mismatch: {id}/{role}.");
  }
  detail=new(text,pipeline.Last?.NormalizedText??text,rawLabel,routeScore,selectedLabel,labelCorrect,pipeline.Last?.Intent.Intent,
   pipeline.Last?.Intent.IsClear??false,decision?.Intent,decision?.SubIntent,decision?.RequiresProvider??false,
   response.SubIntent,response.ErrorCode,blocked,probeLabel,group,null);
 }
 return new(id,role,expected,response.Intent,label,source,correct,response.ProviderState,weight,addedMilliseconds,detail);
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
 if(diagnostics){
  // Counterfactual inference is after all actual turns and bypasses TimingModel;
  // it cannot affect routing, sources, handler results or measured call latency.
  var frozenModel=provider.GetRequiredService<RoleIntentModel>();
  var diagnosticRows=evalRows.Select(row=>row.Diagnostic?.ErrorGroup=="a"
   ?row with{Diagnostic=row.Diagnostic with{Counterfactual=CounterfactualPrediction.From(frozenModel.Predict(row.Diagnostic.NormalizedText,Enum.Parse<AiActorRole>(row.Role)))}}
   :row).ToList();
  var totalWeight=diagnosticRows.Sum(x=>x.Weight);
  var wrong=diagnosticRows.Where(x=>!x.Correct).ToList();
  var wrongWeight=wrong.Sum(x=>x.Weight);
  object Counts(IEnumerable<Row> values,double denominator,double errorDenominator){
   var list=values.ToList();var weight=list.Sum(x=>x.Weight);
   return new{RuntimeTurns=list.Count,WeightedTurns=weight,PercentOfAll=100*weight/denominator,
    PercentOfWrong=errorDenominator==0?0:100*weight/errorDenominator};
  }
  object Groups(IEnumerable<Row> values,double denominator,double errorDenominator)=>"abcdef".ToDictionary(
   c=>c.ToString(),c=>Counts(values.Where(x=>x.Diagnostic!.ErrorGroup==c.ToString()),denominator,errorDenominator));
  var a=wrong.Where(x=>x.Diagnostic!.ErrorGroup=="a").ToList();
  if(a.Any(x=>x.Diagnostic!.Counterfactual is null)) throw new InvalidOperationException("Missing frozen-model counterfactual prediction.");
  var counterfactual=a.GroupBy(x=>x.Diagnostic!.Counterfactual?.Confidence>=.75?">=0.75":"<0.75").Select(g=>new{
   ScoreBand=g.Key,Count=Counts(g,totalWeight,wrongWeight),
   Correct=Counts(g.Where(x=>x.Diagnostic!.Counterfactual?.Label==x.Expected),totalWeight,wrongWeight),
   Wrong=Counts(g.Where(x=>x.Diagnostic!.Counterfactual?.Label!=x.Expected),totalWeight,wrongWeight)}).ToArray();
  if(diagnosticRows.Count!=490 || diagnosticRows.Select(x=>(x.Id,x.Role)).Distinct().Count()!=490 ||
   Math.Abs(totalWeight-240)>1e-8 || wrong.Any(x=>x.Diagnostic?.ErrorGroup is null) ||
   Math.Abs("abcdef".Sum(c=>wrong.Where(x=>x.Diagnostic!.ErrorGroup==c.ToString()).Sum(x=>x.Weight))-wrongWeight)>1e-8)
   throw new InvalidOperationException("Diagnostic coverage/weight/partition invariant failed.");
  output.Add(new{Split="diagnostics",Protocol=new{
   OriginalCases=240,RuntimeTurns=490,TotalWeight=totalWeight,WrongRuntimeTurns=wrong.Count,WrongWeight=wrongWeight,
   ErrorDenominator="Wrong under the unchanged historical handler-label proxy (43.33% accuracy), not verified database execution.",
   LabelDefinition="Committed model label; rule tool/subintent/catalog label, with separate pure existing-planner resource probe when needed. Raw router label is retained. Expected labels never feed mapping. Fallback abstains (null committed label); its logged candidate is not an accepted label.",
   ProbeDefinition="Only after actual response; sentinel resource IDs used solely in a separate AiDeterministicPlanner.Plan call, never resolved/authorized/executed. Does not establish real grounding or override runtime.",
   EvaluationWarning="AI-written eval seen in v1/v2; measurement only; no tuning or retraining."},
   Groups=Groups(wrong,totalWeight,wrongWeight),PerLabel=diagnosticRows.GroupBy(x=>x.Expected).Select(g=>new{
    Label=g.Key,RuntimeTurns=g.Count(),WeightedTurns=g.Sum(x=>x.Weight),WrongRuntimeTurns=g.Count(x=>!x.Correct),
    WrongWeight=g.Where(x=>!x.Correct).Sum(x=>x.Weight),Groups=Groups(g.Where(x=>!x.Correct),g.Sum(x=>x.Weight),g.Where(x=>!x.Correct).Sum(x=>x.Weight))}).ToArray(),
   LabelAccuracyBySource=diagnosticRows.GroupBy(x=>x.Source).Select(g=>new{Source=g.Key,RuntimeTurns=g.Count(),
    WeightedTurns=g.Sum(x=>x.Weight),CorrectRuntimeTurns=g.Count(x=>x.Diagnostic!.LabelCorrect),
    CorrectWeight=g.Where(x=>x.Diagnostic!.LabelCorrect).Sum(x=>x.Weight),
    Accuracy=g.Where(x=>x.Diagnostic!.LabelCorrect).Sum(x=>x.Weight)/g.Sum(x=>x.Weight),
    RawRouterLabelAccuracy=g.Where(x=>x.Diagnostic!.RawRouteLabel==x.Expected).Sum(x=>x.Weight)/g.Sum(x=>x.Weight)}).ToArray(),
   CounterfactualGroupA=new{RuntimeTurns=a.Count,WeightedTurns=a.Sum(x=>x.Weight),
    CorrectRuntimeTurns=a.Count(x=>x.Diagnostic!.Counterfactual?.Label==x.Expected),
    CorrectWeight=a.Where(x=>x.Diagnostic!.Counterfactual?.Label==x.Expected).Sum(x=>x.Weight),ScoreBands=counterfactual},
   Phase5StartBooking=new{Total=phase.Count(x=>x.GetProperty("expectedIntent").GetString()=="StartBooking"),
    StaffActors=phase.Count(x=>x.GetProperty("expectedIntent").GetString()=="StartBooking" && x.GetProperty("actor").GetString()!="Patient")},
   Rows=diagnosticRows});
 }
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
record Row(string Id,string Role,string Expected,string Intent,string Label,string Source,bool Correct,string ProviderState,double Weight,double ModelMilliseconds,Diagnostic? Diagnostic=null);
record Diagnostic(string Text,string NormalizedText,string? RawRouteLabel,double? RouteScore,string? SelectedLabel,bool LabelCorrect,string? ClassifierIntent,bool ClassifierIsClear,
 string? PlannerIntent,string? PlannerSubIntent,bool RequiresProvider,string? ResponseSubIntent,string? ResponseErrorCode,
 string? BlockingReason,string? ResourceProbeLabel,string? ErrorGroup,CounterfactualPrediction? Counterfactual);
record CounterfactualPrediction(string Label,double Confidence){
 public static CounterfactualPrediction? From(RoleIntentModelPrediction? prediction)=>prediction is null?null:new(prediction.Label,prediction.Confidence);
}
sealed class TracePipeline(IAiConversationPipeline inner):IAiConversationPipeline {
 public AiConversationAnalysis? Last{get;private set;}
 public AiConversationAnalysis Analyze(string? rawMessage,IntentClassificationContext? context=null)=>Last=inner.Analyze(rawMessage,context);
}
sealed class TracePlanner(IAiDeterministicPlanner inner):IAiDeterministicPlanner {
 public IAiDeterministicPlanner Inner=>inner; public AiPlannerDecision? Last{get;private set;}
 public AiCopilotPlanningContext? Context{get;private set;}
 public AiPlannerDecision Plan(AiCopilotPlanningContext context){Context=context;return Last=inner.Plan(context);}
 public AiPlannerDecision PlanSuggestion(ClinicManagement.Application.AI.Suggestions.AiSuggestionDefinition? suggestion,AiResolvedResourceContext resource)=>Last=inner.PlanSuggestion(suggestion,resource);
}
sealed class TimingModel(RoleIntentModel inner):IRoleIntentModel {
 public double Milliseconds{get;private set;}
 public bool IsAvailable=>inner.IsAvailable;
 public double Threshold=>inner.Threshold;
 public bool IsLabelAllowed(string label,AiActorRole role)=>inner.IsLabelAllowed(label,role);
 public RoleIntentModelPrediction? Predict(string text,AiActorRole role){var timer=Stopwatch.StartNew();try{return inner.Predict(text,role);}finally{Milliseconds+=timer.Elapsed.TotalMilliseconds;}}
}
