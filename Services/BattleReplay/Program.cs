using Google.Cloud.Firestore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(_options =>
    _options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

string projectId = Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT")
    ?? Environment.GetEnvironmentVariable("GCLOUD_PROJECT")
    ?? "bm-cardbattle";
string databaseId = Environment.GetEnvironmentVariable("FIRESTORE_DATABASE_ID") ?? "cardbattle";

builder.Services.AddSingleton(_ => new FirestoreDbBuilder
{
    ProjectId = projectId,
    DatabaseId = databaseId,
    EmulatorDetection = EmulatorDetection.EmulatorOrProduction,
}.Build());
builder.Services.AddSingleton<BattleSpecRepository>();

WebApplication app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new { ok = true }));

app.MapPost("/v1/battle/replay", async (
    ReplayRequest _request,
    BattleSpecRepository _specs,
    CancellationToken _cancellationToken) =>
{
    if (!_request.TryBuildInput(out BattleReplayInput? t_input, out string t_error))
        return Results.BadRequest(new { ok = false, reason = t_error });

    BattleRuleSet t_rules;
    try
    {
        t_rules = await _specs.GetAsync(
            _request.Env, _request.ContentFingerprint, _request.SpecPins, _cancellationToken);
    }
    catch (ContentFingerprintException t_exception)
    {
        return Results.Conflict(new { ok = false, reason = t_exception.Message });
    }
    catch (SpecLoadException t_exception)
    {
        return Results.Json(new { ok = false, reason = t_exception.Message }, statusCode: 503);
    }

    try
    {
        SynergyRuleProvider.Install(t_rules);
        BattleReplayResult t_result = BattleReplay.Run(t_input!);
        ReplayResponse t_response = ReplayResponse.From(t_result);
        return t_result.Ok ? Results.Ok(t_response) : Results.UnprocessableEntity(t_response);
    }
    finally
    {
        SynergyRuleProvider.Reset();
        MatchRandom.Reset();
    }
});

app.Run();
