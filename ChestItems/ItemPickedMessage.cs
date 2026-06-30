using R2API.Networking.Interfaces;
using RoR2;
using UnityEngine.Networking;

namespace ChestItems 
{
    internal sealed class ItemPickedMessage : INetMessage 
    {
        private NetworkInstanceId targetId;
        private string requestToken;
        private PickupIndex selectedPickup;

        public ItemPickedMessage() 
        {
            requestToken = string.Empty;
        }

        internal ItemPickedMessage(NetworkInstanceId targetId, string requestToken, PickupIndex selectedPickup) 
        {
            this.targetId = targetId;
            this.requestToken = requestToken;
            this.selectedPickup = selectedPickup;
        }

        public void Serialize(NetworkWriter writer) 
        {
            writer.Write(targetId);
            writer.Write(requestToken);
            PickupIndex.WriteToNetworkWriter(writer, selectedPickup);
        }

        public void Deserialize(NetworkReader reader) 
        {
            targetId = reader.ReadNetworkId();
            requestToken = reader.ReadString();
            selectedPickup = PickupIndex.ReadFromNetworkReader(reader);
        }

        public void OnReceived() 
        {
            if (!NetworkServer.active) 
                return;
            

            ChestItemsPlugin.Instance?.HandleItemPicked(targetId, requestToken, selectedPickup);
        }
    }
}