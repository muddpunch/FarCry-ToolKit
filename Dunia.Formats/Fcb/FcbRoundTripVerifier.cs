using System.Security.Cryptography;

namespace Dunia.Formats.Fcb;

public static class FcbRoundTripVerifier
{
    public static FcbRoundTripVerificationResult Verify(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead || !input.CanSeek)
        {
            throw new ArgumentException("FCB input must be readable and seekable.", nameof(input));
        }

        long originalPosition = input.Position;
        try
        {
            FcbDocument document = FcbReader.Read(input);
            using var output = new MemoryStream(
                input.Length <= int.MaxValue ? checked((int)input.Length) : 0);
            FcbWriter.Write(output, document);

            input.Position = 0;
            byte[] sourceHash = SHA256.HashData(input);
            output.Position = 0;
            byte[] outputHash = SHA256.HashData(output);
            bool exact = input.Length == output.Length
                && CryptographicOperations.FixedTimeEquals(sourceHash, outputHash);
            return new(
                input.Length,
                Convert.ToHexString(sourceHash),
                Convert.ToHexString(outputHash),
                exact);
        }
        finally
        {
            input.Position = originalPosition;
        }
    }
}
