using XDrishti.Application.Market.Bars.Eod;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Tests.Market.Bars.Eod;

public sealed class DataEodStepsTests
{
    [Fact]
    public void The_scheduled_run_fetches_aggregates_and_then_checks_the_data_quality_but_does_not_load_csv_files()
    {
        DataEodSteps.Planned().ShouldBe([DataSteps.Dhan, DataSteps.Aggregate, DataSteps.Quality]);
    }

    [Fact]
    public void The_plan_is_every_pipeline_step_but_csv()
    {
        DataEodSteps.Planned().ShouldBe(DataSteps.All.Where(step => step != DataSteps.Csv));
    }

    [Fact]
    public void Every_planned_step_is_one_the_pipeline_accepts()
    {
        DataEodSteps.Planned().ShouldAllBe(step => DataSteps.IsKnown(step));
    }
}

public sealed class DataStepsDefaultTests
{
    [Fact]
    public void Update_data_by_hand_runs_the_three_loading_steps_and_leaves_the_quality_checks_to_a_request_that_names_them()
    {
        DataSteps.Default.ShouldBe([DataSteps.Csv, DataSteps.Dhan, DataSteps.Aggregate]);
        DataSteps.All.ShouldBe([DataSteps.Csv, DataSteps.Dhan, DataSteps.Aggregate, DataSteps.Quality]);
    }
}
