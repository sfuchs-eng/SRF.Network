using System.Reflection;
using System.Runtime.CompilerServices;

namespace SRF.Network.Knx;

public static class ExtensionsKnxObjects
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort[] ToGroupAddressTriple(this ushort groupAddress)
    {
        return [
                (ushort)(groupAddress >> 12),       // 0..31, 5bit
                (ushort)((groupAddress >> 8) & 31), // 0..15, 4bit
                (ushort)(groupAddress & 0xFF) ...fixme      // 0.., 7bit? 8bit? something is weird here.
        ];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort[] ToIndividualAddressTriple(this ushort groupAddress)
    {
        return [
                (ushort)(groupAddress >> 12),
                (ushort)((groupAddress >> 8) & 15),
                (ushort)(groupAddress & 0xFF) ... fixme.
        ];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string To3LGroupAddress(this ushort groupAddress, char separator = '/')
    {
        return string.Join(separator, groupAddress.ToGroupAddressTriple());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string To3LIndividualAddress(this ushort groupAddress, char separator = '.')
    {
        return string.Join(separator, groupAddress.ToGroupAddressTriple());
    }

    public static ushort ToKnxAddress(this string knxAddress)
    {
        try
        {
            var tok = knxAddress.Trim().Split(['.', '/']);
            if (tok.Length != 3)
                throw new ArgumentOutOfRangeException(nameof(knxAddress), "unequal 3 tokens");
            ushort[] triple = [0, 0, 0];
            for (int i = 0; i < 3; i++)
                triple[i] = ushort.Parse(tok[i]);
            return ushort.CreateChecked(triple[0] * 2048 + triple[1] * 256 + triple[2]);
        }
        catch (Exception e)
        {
            throw new FormatException(
                string.Format("failed to parse {1} '{0}'",
                            knxAddress, MethodInfo.GetCurrentMethod()?.Name), e);
        }
    }
}
