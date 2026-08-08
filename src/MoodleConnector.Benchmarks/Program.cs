using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting;
using MoodleConnector.Benchmarks.Cognitive;
using MoodleConnector.Presentation.Configuration;
using OpenAI.Chat;
using Microsoft.Extensions.Configuration;
using System.ClientModel;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using MoodleConnector.Infrastructure;
using System.Linq;
using Microsoft.AspNetCore.Authentication;

namespace MoodleConnector.Benchmarks;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=======================================================");
        Console.WriteLine("  MoodleBench — Courses A × B × C — Experimento 1");
        Console.WriteLine("=======================================================");

        // ------------------------------------------------------------------
        // Environment check
        // ------------------------------------------------------------------
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.WriteLine("ERROR: OPENAI_API_KEY environment variable is not set.");
            Environment.Exit(1);
        }

        // ------------------------------------------------------------------
        // Load tasks — path resolves relative to binary or repo root
        // ------------------------------------------------------------------
        var tasksPath = ResolveTasksPath();
        if (!File.Exists(tasksPath))
        {
            Console.WriteLine($"ERROR: Tasks file not found at {tasksPath}");
            Environment.Exit(1);
        }

        var json = await File.ReadAllTextAsync(tasksPath);
        var tasks = JsonSerializer.Deserialize<List<BenchmarkTask>>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );
        if (tasks == null || tasks.Count == 0)
        {
            Console.WriteLine("ERROR: No tasks loaded from tasks file.");
            Environment.Exit(1);
        }

        Console.WriteLine($"Loaded {tasks.Count} tasks from {tasksPath}");
        Console.WriteLine($"CommitSha: {(string.IsNullOrWhiteSpace(OpenAIResponsesBenchmarkDriver.CommitSha) ? "(unknown)" : OpenAIResponsesBenchmarkDriver.CommitSha[..Math.Min(7, OpenAIResponsesBenchmarkDriver.CommitSha.Length)])}");
        Console.WriteLine();

        // ------------------------------------------------------------------
        // Output dirs
        // ------------------------------------------------------------------
        var runId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var tracesDir = ResolveOutputDir(Path.Combine(".moodlebench", "cognitive", "traces"));
        var reportsDir = ResolveOutputDir(Path.Combine(".moodlebench", "cognitive", "reports", runId));
        Directory.CreateDirectory(tracesDir);
        Directory.CreateDirectory(reportsDir);

        // ------------------------------------------------------------------
        // Profiles
        // ------------------------------------------------------------------
        var profilesToRun = new[]
        {
            new BenchmarkProfile(ToolExposureProfile.Full,                "gpt-4o", false), // A — baseline
            new BenchmarkProfile(ToolExposureProfile.FullWithCoursesSkill,"gpt-4o", true),  // B
            new BenchmarkProfile(ToolExposureProfile.SkillCoursesOptimized,"gpt-4o", true), // C
        };

        var chatClient = new ChatClient("gpt-4o", new ApiKeyCredential(apiKey));
        var allTraces = new Dictionary<ToolExposureProfile, List<CognitiveTrace>>();

        // ------------------------------------------------------------------
        // Run
        // ------------------------------------------------------------------
        foreach (var profile in profilesToRun)
        {
            Console.WriteLine($"=======================================================");
            Console.WriteLine($"  Profile {ProfileLabel(profile.Exposure)} — {profile.Exposure}");
            Console.WriteLine($"=======================================================");

            using var factory = BuildFactory(profile);

            var mcpClient = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
            mcpClient.DefaultRequestHeaders.Add("X-Mcp-Api-Key", "test-key");

            var driver = new OpenAIResponsesBenchmarkDriver(chatClient, mcpClient);
            var profileTraces = new List<CognitiveTrace>();

            int successCount = 0;
            int taskIndex = 0;
            foreach (var task in tasks)
            {
                // Inter-task delay to respect OpenAI TPM limits (30k/min).
                // Each task with 97 tools consumes ~13k tokens. Without delay,
                // ~2 tasks exhaust the budget and trigger 429 errors.
                if (taskIndex > 0)
                    await Task.Delay(TimeSpan.FromSeconds(2));
                taskIndex++;

                Console.Write($"  [{task.Id}] {(task.IsCriticalTask ? "⚠️ " : "")}");
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
                try
                {
                    var trace = await driver.RunAsync(task, profile, cts.Token);
                    profileTraces.Add(trace);

                    // Save individual trace
                    var traceFile = Path.Combine(tracesDir, $"{profile.Exposure}_{task.Id}.json");
                    await File.WriteAllTextAsync(traceFile, JsonSerializer.Serialize(
                        trace, new JsonSerializerOptions { WriteIndented = true }));

                    if (trace.Scoring.OverallSuccess)
                    {
                        successCount++;
                        Console.WriteLine($"✅ (Tools: {trace.Routing.ToolInvocations.Count}, " +
                                          $"SchemaTokens: {trace.Execution.ToolSchemaTokens}, " +
                                          $"Latency: {trace.Execution.LatencyMs}ms)");
                    }
                    else
                    {
                        var flags = new List<string> { $"Reason: {trace.Scoring.FailureReason}" };
                        if (trace.Scoring.WrongConnectionDetected) flags.Add("WrongConn");
                        if (trace.Scoring.HallucinationDetected)   flags.Add("Hallucination");
                        Console.WriteLine($"❌ ({string.Join(", ", flags)})");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"💥 ERROR ({ex.Message})");
                }
            }

            allTraces[profile.Exposure] = profileTraces;
            Console.WriteLine($"\n  Summary [{ProfileLabel(profile.Exposure)}]: {successCount}/{tasks.Count} succeeded");
            Console.WriteLine();
        }

        // ------------------------------------------------------------------
        // Build reports
        // ------------------------------------------------------------------
        Console.WriteLine("Building report...");

        var reportA = ProfileReportBuilder.Build(ToolExposureProfile.Full,                 "gpt-4o", allTraces.GetValueOrDefault(ToolExposureProfile.Full,                  new()) ?? new());
        var reportB = ProfileReportBuilder.Build(ToolExposureProfile.FullWithCoursesSkill, "gpt-4o", allTraces.GetValueOrDefault(ToolExposureProfile.FullWithCoursesSkill,  new()) ?? new());
        var reportC = ProfileReportBuilder.Build(ToolExposureProfile.SkillCoursesOptimized,"gpt-4o", allTraces.GetValueOrDefault(ToolExposureProfile.SkillCoursesOptimized, new()) ?? new());

        var evaluator = new BenchmarkGateEvaluator();
        var gatesB = evaluator.EvaluateAgainstBaseline(reportA, reportB);
        var gatesC = evaluator.EvaluateAgainstBaseline(reportA, reportC);

        var report = new BenchmarkReport(
            RunId: runId,
            BenchmarkVersion: OpenAIResponsesBenchmarkDriver.BenchmarkVersion,
            CommitSha: OpenAIResponsesBenchmarkDriver.CommitSha,
            Model: "gpt-4o",
            ProfileA: reportA,
            ProfileB: reportB,
            ProfileC: reportC,
            GatesForProfileB: gatesB,
            GatesForProfileC: gatesC,
            ProfileBApproved: gatesB.All(g => g.Passed),
            ProfileCApproved: gatesC.All(g => g.Passed)
        );

        // ------------------------------------------------------------------
        // Save JSON report
        // ------------------------------------------------------------------
        var reportJsonPath = Path.Combine(reportsDir, "report.json");
        await File.WriteAllTextAsync(reportJsonPath, JsonSerializer.Serialize(
            report, new JsonSerializerOptions { WriteIndented = true }));

        // ------------------------------------------------------------------
        // Save Markdown report
        // ------------------------------------------------------------------
        var markdown = BenchmarkReportRenderer.RenderMarkdown(report);
        var reportMdPath = Path.Combine(reportsDir, "report.md");
        await File.WriteAllTextAsync(reportMdPath, markdown);

        // ------------------------------------------------------------------
        // Console summary
        // ------------------------------------------------------------------
        Console.WriteLine();
        Console.WriteLine("=======================================================");
        Console.WriteLine("  GATE SUMMARY — Profile C vs Baseline A");
        Console.WriteLine("=======================================================");
        foreach (var gate in gatesC)
        {
            var icon = gate.Passed ? "✅" : "❌";
            Console.WriteLine($"  {icon} {gate.Description}");
            Console.WriteLine($"     Baseline: {gate.BaselineValue}  |  C: {gate.ProfileValue}  |  Threshold: {gate.Threshold}");
        }
        Console.WriteLine();

        var verdict = report.ProfileCApproved
            ? "✅ APPROVED — Profile C passa todos os gates. Wrappers de Courses podem ser removidas."
            : "❌ REJECTED — Profile C falhou em um ou mais gates. Investigar antes de remover wrappers.";

        Console.WriteLine($"VEREDICTO: {verdict}");
        Console.WriteLine();
        Console.WriteLine($"Traces  → {tracesDir}");
        Console.WriteLine($"Report  → {reportMdPath}");
        Console.WriteLine();
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static string ProfileLabel(ToolExposureProfile p) => p switch
    {
        ToolExposureProfile.Full                  => "A",
        ToolExposureProfile.FullWithCoursesSkill  => "B",
        ToolExposureProfile.SkillCoursesOptimized => "C",
        _ => p.ToString()
    };

    private static string ResolveTasksPath()
    {
        // Try relative to binary first (dotnet run from project dir)
        var fromBinary = Path.Combine(
            AppContext.BaseDirectory,
            "Cognitive", "Tasks", "Courses", "CoursesTasks.json");
        if (File.Exists(fromBinary)) return fromBinary;

        // Try relative to repo root (dotnet run from repo root)
        var fromRoot = Path.Combine(
            Environment.CurrentDirectory,
            "src", "MoodleConnector.Benchmarks", "Cognitive", "Tasks", "Courses", "CoursesTasks.json");
        if (File.Exists(fromRoot)) return fromRoot;

        // Try going up from binary until we find the repo root
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "MoodleConnector.Benchmarks",
                "Cognitive", "Tasks", "Courses", "CoursesTasks.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }

        return fromRoot; // return best guess; caller will check existence
    }

    private static string ResolveOutputDir(string relative)
    {
        // Prefer placing output at repo root
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MoodleConnector.sln")))
                return Path.Combine(dir.FullName, relative);
            dir = dir.Parent;
        }
        return Path.Combine(Environment.CurrentDirectory, relative);
    }

    private static WebApplicationFactory<global::Program> BuildFactory(BenchmarkProfile profile)
    {
        return new WebApplicationFactory<global::Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");

                // Resolve content root for Presentation project
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                string? contentRoot = null;
                while (dir != null)
                {
                    var candidate = Path.Combine(dir.FullName, "src", "MoodleConnector.Presentation");
                    if (Directory.Exists(candidate)) { contentRoot = candidate; break; }
                    dir = dir.Parent;
                }
                if (contentRoot != null)
                    builder.UseContentRoot(contentRoot);

                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        { "MCP_EXPOSURE_PROFILE", profile.Exposure.ToString() },
                        { "McpServerSecurity:RequireApiKey", "true" },
                        { "McpServerSecurity:RequireJwt", "false" }
                    });
                });

                builder.ConfigureTestServices(services =>
                {
                    // Replace EF Core / Npgsql with in-memory DB
                    var efDescriptors = services.Where(d =>
                        d.ServiceType.Namespace != null &&
                        (d.ServiceType.Namespace.StartsWith("Microsoft.EntityFrameworkCore") ||
                         d.ServiceType.Namespace.StartsWith("Npgsql")))
                        .ToList();
                    foreach (var descriptor in efDescriptors)
                        services.Remove(descriptor);

                    services.AddDbContext<ConnectorDbContext>(options =>
                        options.UseInMemoryDatabase("InMemoryDbForTesting"));

                    services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = "Test";
                        options.DefaultChallengeScheme = "Test";
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });

                    services.AddSingleton<
                        MoodleConnector.Application.Abstractions.IMcpConnectorClientResolver,
                        FakeConnectorClientResolver>();

                    services.PostConfigure<Microsoft.AspNetCore.Authorization.AuthorizationOptions>(options =>
                        options.FallbackPolicy = null);
                });
            });
    }
}
