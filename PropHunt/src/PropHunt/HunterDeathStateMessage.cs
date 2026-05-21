using Mirror;

namespace PropHunt;

internal struct HunterDeathStateMessage : NetworkMessage
{
    public ulong HunterGuid;
    public bool IsDead;
}

internal static class HunterDeathStateMessageSerializer
{
    private static bool registered;

    public static void Register()
    {
        if (registered)
            return;

        Writer<HunterDeathStateMessage>.write = Write;
        Reader<HunterDeathStateMessage>.read = Read;
        registered = true;
    }

    private static void Write(NetworkWriter writer, HunterDeathStateMessage message)
    {
        writer.WriteULong(message.HunterGuid);
        writer.WriteBool(message.IsDead);
    }

    private static HunterDeathStateMessage Read(NetworkReader reader)
    {
        return new HunterDeathStateMessage
        {
            HunterGuid = reader.ReadULong(),
            IsDead = reader.ReadBool()
        };
    }
}
