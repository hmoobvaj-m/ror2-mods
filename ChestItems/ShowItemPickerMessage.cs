using System.Collections.Generic;
using R2API.Networking.Interfaces;
using RoR2;
using UnityEngine.Networking;

namespace ChestItems 
{
    internal sealed class ShowItemPickerMessage : INetMessage 
    {
        private NetworkInstanceId targetId;
        private string requestToken;
        private List<PickupIndex> pickups;

        public ShowItemPickerMessage() 
        {
            requestToken = string.Empty;
            pickups = new List<PickupIndex>();
        }

        internal ShowItemPickerMessage(NetworkInstanceId targetId, string requestToken, List<PickupIndex> pickups) 
        {
            this.targetId = targetId;
            this.requestToken = requestToken;
            this.pickups = pickups;
        }

        public void Serialize(NetworkWriter writer) 
        {
            writer.Write(targetId);
            writer.Write(requestToken);
            writer.Write(pickups.Count);

            foreach (PickupIndex pickup in pickups) 
            {
                PickupIndex.WriteToNetworkWriter(writer, pickup);
            }
        }

        public void Deserialize(NetworkReader reader) 
        {
            targetId = reader.ReadNetworkId();
            requestToken = reader.ReadString();

            int count = reader.ReadInt32();
            pickups = new List<PickupIndex>(count);

            for (int i = 0; i < count; i++) 
            {
                pickups.Add(PickupIndex.ReadFromNetworkReader(reader));
            }
        }

        public void OnReceived() 
        {
            ChestItemsPlugin.Instance?.HandleShowItemPicker(targetId, requestToken, pickups);
        }
    }
}