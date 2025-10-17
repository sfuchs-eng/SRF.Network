using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SRF.Network.Knx;

public static class ExtensionsKnxObjects
{
    internal class KnxAddressLevelMask(ushort mask, byte shift)
    {
        public ushort Mask { get; } = mask;
        public byte Shift { get; } = shift;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort GetLevelAddress(ushort fullAddress)
        {
            return (ushort)((fullAddress & Mask) >> Shift);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort GetFullAddress(ushort levelAddressValue)
        {
            return (ushort)((levelAddressValue << Shift) & Mask);
        }
    }

    internal static readonly Dictionary<int,KnxAddressLevelMask> GroupAddressMasks = new()
    {
        [1] = new(0b_0000_0000_1111_1111, 0), // level 1
        [2] = new(0b_0000_0111_0000_0000, 8), // level 2
        [3] = new(0b_1111_1000_0000_0000, 11),// level 3
    };

    internal static readonly Dictionary<int, KnxAddressLevelMask> IndividualAddressMasks = new()
    {
        [1] = new(0b_0000_0000_1111_1111, 0), // level 1
        [2] = new(0b_0000_1111_0000_0000, 8), // level 2
        [3] = new(0b_1111_0000_0000_0000, 12), // level 3
    };
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort[] ToGroupAddressTriple(this ushort groupAddress)
    {
        int[] index = [3,2,1];
        return [.. index.Select(i => GroupAddressMasks[i].GetLevelAddress(groupAddress))];
    }

    public static bool IsExtendedGroupAddress(this ushort groupAddress)
    {
        return (groupAddress & (1 << 14)) > 0; // bit 15 set?
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ushort[] ToIndividualAddressTriple(this ushort groupAddress)
    {
        int[] index = [3,2,1];
        return [.. index.Select(i => IndividualAddressMasks[i].GetLevelAddress(groupAddress))];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string To3LGroupAddress(this ushort groupAddress, char separator = '/')
    {
        return string.Join(separator, groupAddress.ToGroupAddressTriple());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string To3LIndividualAddress(this ushort groupAddress, char separator = '.')
    {
        return string.Join(separator, groupAddress.ToIndividualAddressTriple());
    }

    internal static ushort ToKnxAddress(this string knxAddress, Dictionary<int,KnxAddressLevelMask> demasker)
    {
        try
        {
            var tok = knxAddress.Trim().Split(['.', '/']);
            if (tok.Length != 3)
                throw new ArgumentOutOfRangeException(nameof(knxAddress), "unequal 3 tokens");
            ushort[] triple = [0, 0, 0];
            for (int i = 0; i < 3; i++)
                triple[i] = ushort.Parse(tok[i]);
            int[] index = [1, 2, 3];
            return ushort.CreateChecked(index.Select(i => (int)demasker[i].GetFullAddress(triple[3 - i])).Sum());
        }
        catch (Exception e)
        {
            throw new FormatException(
                string.Format("failed to parse {1} '{0}'",
                            knxAddress, MethodInfo.GetCurrentMethod()?.Name), e);
        }
    }
}
