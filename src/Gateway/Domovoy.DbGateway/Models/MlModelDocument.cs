using Domovoy.Contracts.Ml;

namespace Domovoy.DbGateway.Models;

/// <summary>
/// Persisted ML model (roadmap Epic 2A): the <see cref="MlModel"/> metadata plus the serialized ML.NET
/// model artifact (small for the v1 regression — stored inline as BSON binary). Lives in the
/// <c>ml_models</c> collection; the artifact is fetched separately so list responses stay light.
/// </summary>
public class MlModelDocument : MlModel
{
    public byte[] Artifact { get; set; } = Array.Empty<byte>();
}
