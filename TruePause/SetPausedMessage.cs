using R2API.Networking.Interfaces;
using UnityEngine.Networking;

namespace TruePause
{
    public class SetPausedMessage : INetMessage
    {
        private bool paused;

        public SetPausedMessage()
        {
        }

        public SetPausedMessage(bool paused)
        {
            this.paused = paused;
        }

        public void Serialize(NetworkWriter writer)
        {
            writer.Write(paused);
        }

        public void Deserialize(NetworkReader reader)
        {
            paused = reader.ReadBoolean();
        }

        public void OnReceived()
        {
            TruePausePlugin.ReceivePaused(paused);
        }
    }
}