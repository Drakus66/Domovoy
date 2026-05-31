using System.Security.Cryptography;
using System.Text;

namespace Domovoy.Contracts.Devices;

/// <summary>
/// Derives a STABLE, deterministic device id from an adapter source + hardware id (e.g. Zigbee IEEE).
/// A deterministic id means re-announcements and bridge restarts resolve to the SAME logical device
/// without relying on an in-memory lookup — eliminating the "duplicate device on restart" problem.
/// Implemented as an RFC 4122 §4.3 name-based (v5 / SHA-1) UUID over a fixed Domovoy namespace.
/// </summary>
public static class DeviceIdFactory
{
    // Fixed, arbitrary namespace GUID for Domovoy device identities. Do not change.
    private static readonly Guid Namespace = new("8f2e7d1a-9c4b-4a6f-b3e2-1d0c5a7b6e94");

    /// <summary>Stable device id for <paramref name="hardwareId"/> owned by <paramref name="adapterSource"/>.</summary>
    public static Guid Derive(string adapterSource, string hardwareId)
    {
        if (string.IsNullOrWhiteSpace(adapterSource))
            throw new ArgumentException("Adapter source is required.", nameof(adapterSource));
        if (string.IsNullOrWhiteSpace(hardwareId))
            throw new ArgumentException("Hardware id is required.", nameof(hardwareId));

        return CreateNameBasedV5(Namespace, $"{adapterSource}|{hardwareId}");
    }

    private static Guid CreateNameBasedV5(Guid ns, string name)
    {
        var nsBytes = ns.ToByteArray();
        SwapEndianness(nsBytes);

        var nameBytes = Encoding.UTF8.GetBytes(name);
        var data = new byte[nsBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(nsBytes, 0, data, 0, nsBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, data, nsBytes.Length, nameBytes.Length);

        var hash = SHA1.HashData(data);

        var guid = new byte[16];
        Array.Copy(hash, 0, guid, 0, 16);
        guid[6] = (byte)((guid[6] & 0x0F) | 0x50); // version 5
        guid[8] = (byte)((guid[8] & 0x3F) | 0x80); // RFC 4122 variant

        SwapEndianness(guid); // back to the little-endian layout System.Guid expects
        return new Guid(guid);
    }

    // Swaps the byte order of the first three GUID fields (little-endian <-> network order).
    private static void SwapEndianness(byte[] g)
    {
        (g[0], g[3]) = (g[3], g[0]);
        (g[1], g[2]) = (g[2], g[1]);
        (g[4], g[5]) = (g[5], g[4]);
        (g[6], g[7]) = (g[7], g[6]);
    }
}
