using R2API.Networking.Interfaces;
using RoR2;
using UnityEngine.Networking;

namespace ChestItems 
{
    internal sealed class ItemPickedMessage : INetMessage 
    {
        private NetworkInstanceId targetId;
        private PickupIndex selectedPickup;

        public ItemPickedMessage() 
        {
        }

        internal ItemPickedMessage(NetworkInstanceId targetId, PickupIndex selectedPickup) 
        {
            this.targetId = targetId;
            this.selectedPickup = selectedPickup;
        }

        public void Serialize(NetworkWriter writer) 
        {
            writer.Write(targetId);
            PickupIndex.WriteToNetworkWriter(writer, selectedPickup);
        }

        public void Deserialize(NetworkReader reader) 
        {
            targetId = reader.ReadNetworkId();
            selectedPickup = PickupIndex.ReadFromNetworkReader(reader);
        }

        public void OnReceived() 
        {
            if (!NetworkServer.active) 
            {
                return;
            }

            ChestItemsPlugin.Instance?.HandleItemPicked(targetId, selectedPickup);
        }
    }
}