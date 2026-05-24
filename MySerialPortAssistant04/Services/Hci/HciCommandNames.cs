namespace MySerialPortAssistant04.Services.Hci;

/// <summary>
/// HCI 命令 OGF/OCF 可读名称映射。
/// </summary>
public static class HciCommandNames
{
    public static string GetOgfName(byte ogf) => ogf switch
    {
        0x00 => "N/A",
        0x01 => "LinkControl",
        0x02 => "LinkPolicy",
        0x03 => "ControllerBaseband",
        0x04 => "Informational",
        0x05 => "StatusParams",
        0x06 => "Testing",
        0x08 => "LEController",
        0x3F => "VendorDebug",
        _ => $"Unknown(0x{ogf:X2})"
    };

    public static string GetCommandName(byte ogf, ushort ocf)
    {
        if (ogf == 0x01)
        {
            return ocf switch
            {
                0x0001 => "Inquiry",
                0x0002 => "InquiryCancel",
                0x0005 => "CreateConnection",
                0x0006 => "Disconnect",
                _ => $"LinkCtrl_0x{ocf:X3}"
            };
        }

        if (ogf == 0x08)
        {
            return ocf switch
            {
                0x0001 => "LE_SetEventMask",
                0x0005 => "LE_SetAdvertisingParams",
                0x0007 => "LE_SetAdvertisingData",
                0x0009 => "LE_SetAdvertiseEnable",
                0x000C => "LE_CreateConnection",
                _ => $"LE_0x{ocf:X3}"
            };
        }

        return $"CMD_0x{ocf:X3}";
    }
}
