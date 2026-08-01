using Aggregator.Monitoring;

namespace Aggregator.Hosting;

/// <summary>
/// The pipeline assembled in the composition root, paired with the metrics view over its stages.
/// Exists because the stages are built from the runtime <c>Sources</c> list (so they cannot be
/// registered in DI individually) yet must be shared between the hosted pipeline and the stats
/// reporter — this record carries both out of the single step that builds them.
/// </summary>
public sealed record PipelineComponents(AggregatorPipeline Pipeline, IMetricsSource Metrics);
