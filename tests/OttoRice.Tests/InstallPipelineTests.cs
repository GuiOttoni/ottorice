using OttoRice.Common;
using OttoRice.Features.ThemeImport.Models;
using OttoRice.Features.ThemeInstall;
using OttoRice.Features.ThemeInstall.Steps;

namespace OttoRice.Tests;

public class InstallPipelineTests
{
    private sealed class FakeStep(string name, bool succeeds = true, bool throws = false) : IInstallStep
    {
        public string Name => name;
        public List<string> Log { get; init; } = [];

        public Task<Result> ExecuteAsync(InstallContext context, CancellationToken ct = default)
        {
            Log.Add($"exec:{name}");
            if (throws) throw new InvalidOperationException("boom");
            return Task.FromResult(succeeds ? Result.Ok() : Result.Fail($"{name} falhou"));
        }

        public Task CompensateAsync(InstallContext context)
        {
            Log.Add($"comp:{name}");
            return Task.CompletedTask;
        }
    }

    private static InstallContext Context() => new()
    {
        Manifest = new RiceManifest { ThemeId = "t", Name = "T" },
        ThemeDirectory = Path.GetTempPath(),
    };

    [Fact]
    public async Task All_steps_succeed_no_compensation()
    {
        var log = new List<string>();
        var steps = new[] { new FakeStep("a") { Log = log }, new FakeStep("b") { Log = log } };

        var result = await new InstallPipeline(steps).RunAsync(Context());

        Assert.True(result.IsSuccess);
        Assert.Equal(["exec:a", "exec:b"], log);
    }

    [Fact]
    public async Task Failure_compensates_executed_steps_in_reverse_including_failed_one()
    {
        var log = new List<string>();
        var steps = new[]
        {
            new FakeStep("a") { Log = log },
            new FakeStep("b") { Log = log },
            new FakeStep("c", succeeds: false) { Log = log },
            new FakeStep("d") { Log = log },
        };

        var result = await new InstallPipeline(steps).RunAsync(Context());

        Assert.False(result.IsSuccess);
        Assert.Contains("c falhou", result.Error);
        Assert.Equal(["exec:a", "exec:b", "exec:c", "comp:c", "comp:b", "comp:a"], log);
    }

    [Fact]
    public async Task Exception_in_step_is_converted_to_failure_and_compensated()
    {
        var log = new List<string>();
        var steps = new[] { new FakeStep("a") { Log = log }, new FakeStep("b", throws: true) { Log = log } };

        var result = await new InstallPipeline(steps).RunAsync(Context());

        Assert.False(result.IsSuccess);
        Assert.Contains("boom", result.Error);
        Assert.Equal(["exec:a", "exec:b", "comp:b", "comp:a"], log);
    }

    [Fact]
    public async Task Compensation_failure_does_not_stop_remaining_compensations()
    {
        var log = new List<string>();
        var badCompensation = new ThrowingCompensationStep { Log = log };
        var steps = new IInstallStep[]
        {
            new FakeStep("a") { Log = log },
            badCompensation,
            new FakeStep("c", succeeds: false) { Log = log },
        };

        var result = await new InstallPipeline(steps).RunAsync(Context());

        Assert.False(result.IsSuccess);
        Assert.Contains("comp:a", log); // compensação de 'a' rodou apesar da falha em 'b'
    }

    private sealed class ThrowingCompensationStep : IInstallStep
    {
        public string Name => "b";
        public List<string> Log { get; init; } = [];

        public Task<Result> ExecuteAsync(InstallContext context, CancellationToken ct = default)
        {
            Log.Add("exec:b");
            return Task.FromResult(Result.Ok());
        }

        public Task CompensateAsync(InstallContext context) =>
            throw new InvalidOperationException("compensação quebrada");
    }

    [Fact]
    public void StepNames_exposes_step_names_in_order()
    {
        var pipeline = new InstallPipeline([new FakeStep("a"), new FakeStep("b"), new FakeStep("c")]);
        Assert.Equal(["a", "b", "c"], pipeline.StepNames);
    }

    [Fact]
    public async Task Step_state_transitions_report_running_success_and_the_failed_step_stays_failed()
    {
        var states = new List<(string Name, InstallStepState State)>();
        var steps = new[]
        {
            new FakeStep("a"),
            new FakeStep("b", succeeds: false),
        };
        var context = new InstallContext
        {
            Manifest = new RiceManifest { ThemeId = "t", Name = "T" },
            ThemeDirectory = Path.GetTempPath(),
            StepStateChanged = (name, state) => states.Add((name, state)),
        };

        await new InstallPipeline(steps).RunAsync(context);

        Assert.Equal(
            [
                ("a", InstallStepState.Running),
                ("a", InstallStepState.Success),
                ("b", InstallStepState.Running),
                ("b", InstallStepState.Failed),
                ("a", InstallStepState.Compensated),
            ],
            states);
        // 'b' (o que falhou) nunca vira "Compensated" — fica "Failed" pra indicar a causa.
        Assert.DoesNotContain(states, s => s.Name == "b" && s.State == InstallStepState.Compensated);
    }
}
