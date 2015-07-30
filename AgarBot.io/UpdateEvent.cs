namespace AgarBot
{
    public struct UpdateEvent
    {
        public uint player_id;

        public uint x;
        public uint y;

        public ushort radius;

        public byte red, green, blue;

        public byte flags;

        public string skin_url;
        public string name;
    }
}