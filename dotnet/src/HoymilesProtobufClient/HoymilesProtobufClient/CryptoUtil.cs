using System.Buffers.Binary;
using System.Security.Cryptography;

namespace HoymilesProtobufClient;

internal static class CryptoUtil
{
    public static byte[] DeriveAes128Key(byte[] encRand)
    {
        ArgumentNullException.ThrowIfNull(encRand);
        if (encRand.Length != 16)
        {
            throw new ArgumentException("encRand must be 16 bytes.", nameof(encRand));
        }

        byte[] hash = Sha256(Sha256(Sha256(encRand)));
        return hash[..16];
    }

    public static byte[] DeriveNonce(byte[] encRand, ushort tag, ushort sequence)
    {
        ArgumentNullException.ThrowIfNull(encRand);
        if (encRand.Length != 16)
        {
            throw new ArgumentException("encRand must be 16 bytes.", nameof(encRand));
        }

        Span<byte> buffer = stackalloc byte[20];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, tag);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[2..], sequence);
        encRand.CopyTo(buffer[4..]);

        byte[] hash = Sha256(Sha256(Sha256(buffer)));
        return hash[^12..];
    }

    public static byte[] CryptData(bool encrypt, byte[] encRand, ushort tag, ushort sequence, byte[] input)
    {
        ArgumentNullException.ThrowIfNull(input);

        byte[] key = DeriveAes128Key(encRand);
        byte[] nonce = DeriveNonce(encRand, tag, sequence);

        Span<byte> aad = stackalloc byte[4];
        BinaryPrimitives.WriteUInt16LittleEndian(aad, tag);
        BinaryPrimitives.WriteUInt16LittleEndian(aad[2..], sequence);

        using var aesGcm = new AesGcm(key, 16);

        if (encrypt)
        {
            byte[] ciphertext = new byte[input.Length];
            byte[] tagBytes = new byte[16];
            aesGcm.Encrypt(nonce, input, ciphertext, tagBytes, aad);

            byte[] output = new byte[ciphertext.Length + tagBytes.Length];
            Buffer.BlockCopy(ciphertext, 0, output, 0, ciphertext.Length);
            Buffer.BlockCopy(tagBytes, 0, output, ciphertext.Length, tagBytes.Length);
            return output;
        }

        if (input.Length < 16)
        {
            throw new ArgumentException("Encrypted payload too short.", nameof(input));
        }

        int cipherLen = input.Length - 16;
        byte[] cipher = input[..cipherLen];
        byte[] tagPart = input[cipherLen..];
        byte[] plain = new byte[cipherLen];
        aesGcm.Decrypt(nonce, cipher, tagPart, plain, aad);

        return plain;
    }

    private static byte[] Sha256(ReadOnlySpan<byte> data)
    {
        return SHA256.HashData(data);
    }
}
