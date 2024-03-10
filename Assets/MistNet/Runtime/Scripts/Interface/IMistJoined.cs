namespace MistNet
{
    internal interface IMistJoined
    {
        void OnJoined(string id);
    }

    internal interface IMistLeft
    {
        void OnLeft(string id);
    }
}