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
        Console.WriteLine("Initializing MoodleBench Cognitive Experimento 1");

        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            Console.WriteLine("ERROR: OPENAI_API_KEY environment variable is not set. Please set it before running the benchmark.");
            return;
        }

        var tasksPath = Path.Combine(Environment.CurrentDirectory, "src", "MoodleConnector.Benchmarks", "Cognitive", "Tasks", "Courses", "CoursesTasks.json");
        if (!File.Exists(tasksPath))
        {
            Console.WriteLine($"Tasks file not found at {tasksPath}");
            return;
        }

        var json = await File.ReadAllTextAsync(tasksPath);
        var tasks = JsonSerializer.Deserialize<List<BenchmarkTask>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (tasks == null || tasks.Count == 0)
        {
            Console.WriteLine("No tasks loaded.");
            return;
        }

        var tracesDir = Path.Combine(Environment.CurrentDirectory, ".moodlebench", "cognitive", "traces");
        Directory.CreateDirectory(tracesDir);

        var profilesToRun = new[]
        {
            new BenchmarkProfile(ToolExposureProfile.Full, "gpt-4o", false), // Profile A (Baseline 97 tools)
            new BenchmarkProfile(ToolExposureProfile.FullWithCoursesSkill, "gpt-4o", true), // Profile B (Full + courses skill)
            new BenchmarkProfile(ToolExposureProfile.SkillCoursesOptimized, "gpt-4o", true)  // Profile C (SKILL focused)
        };

        var chatClient = new ChatClient("gpt-4o", new ApiKeyCredential(apiKey));

        foreach (var profile in profilesToRun)
        {
            Console.WriteLine($"\n=======================================================");
            Console.WriteLine($"Booting MCP Server for Profile: {profile.Exposure}...");
            
            // Create a dedicated WebApplicationFactory for this profile to ensure clean DI and tool mapping
            using var factory = new WebApplicationFactory<global::Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Testing");
                    builder.UseContentRoot(Path.Combine(Environment.CurrentDirectory, "src", "MoodleConnector.Presentation"));
                    builder.ConfigureAppConfiguration((context, config) =>
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
                        var efDescriptors = services.Where(d => 
                            d.ServiceType.Namespace != null && 
                            (d.ServiceType.Namespace.StartsWith("Microsoft.EntityFrameworkCore") || 
                             d.ServiceType.Namespace.StartsWith("Npgsql")))
                            .ToList();
                            
                        foreach (var descriptor in efDescriptors)
                        {
                            services.Remove(descriptor);
                        }

                        services.AddDbContext<ConnectorDbContext>(options =>
                        {
                            options.UseInMemoryDatabase("InMemoryDbForTesting");
                        });

                        services.AddAuthentication(options =>
                        {
                            options.DefaultAuthenticateScheme = "Test";
                            options.DefaultChallengeScheme = "Test";
                        })
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
                        services.AddSingleton<MoodleConnector.Application.Abstractions.IMcpConnectorClientResolver, FakeConnectorClientResolver>();
                        
                        services.PostConfigure<Microsoft.AspNetCore.Authorization.AuthorizationOptions>(options =>
                        {
                            options.FallbackPolicy = null;
                        });
                    });
                });

            var mcpClient = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });
            mcpClient.DefaultRequestHeaders.Add("X-Mcp-Api-Key", "test-key");
            var driver = new OpenAIResponsesBenchmarkDriver(chatClient, mcpClient);

            Console.WriteLine($"Running {tasks.Count} tasks for {profile.Exposure}...");
            int successCount = 0;

            foreach (var task in tasks)
            {
                Console.Write($"  - [{task.Id}] ");
                
                var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                try
                {
                    var trace = await driver.RunAsync(task, profile, cts.Token);
                    
                    // Save trace with profile prefix
                    var traceFile = Path.Combine(tracesDir, $"{profile.Exposure}_{task.Id}.json");
                    await File.WriteAllTextAsync(traceFile, JsonSerializer.Serialize(trace, new JsonSerializerOptions { WriteIndented = true }));
                    
                    if (trace.Scoring.OverallSuccess)
                    {
                        successCount++;
                        Console.WriteLine($"SUCCESS (Tools: {trace.Execution.MoodleCalls}, Latency: {trace.Execution.LatencyMs}ms)");
                    }
                    else
                    {
                        Console.WriteLine($"FAILED (Reason: {trace.Scoring.FailureReason})");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR ({ex.Message})");
                }
            }
            
            Console.WriteLine($"\nProfile {profile.Exposure} Summary: {successCount}/{tasks.Count} Tasks Succeeded.");
        }

        Console.WriteLine("\nExperimento 1 run complete. Traces saved to .moodlebench/cognitive/traces.");
    }
}
