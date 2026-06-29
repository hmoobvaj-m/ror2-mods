using System.Collections.Generic;
using R2API.Networking.Interfaces;
using RoR2;
using UnityEngine.Networking;

namespace ChestItems 
{
    internal sealed class ShowItemPickerMessage : INetMessage 
    {
        private NetworkInstanceId targetId;
        private List<PickupIndex> pickups;

        public ShowItemPickerMessage() 
        {
            pickups = new List<PickupIndex>();
        }

        internal ShowItemPickerMessage(NetworkInstanceId targetId, List<PickupIndex> pickups) 
        {
            this.targetId = targetId;
            this.pickups = pickups;
        }

        public void Serialize(NetworkWriter writer) 
        {
            writer.Write(targetId);
            writer.Write(pickups.Count);

            foreach (PickupIndex pickup in pickups) 
            {
                PickupIndex.WriteToNetworkWriter(writer, pickup);
            }
        }

        public void Deserialize(NetworkReader reader) 
        {
            targetId = reader.ReadNetworkId();

            int count = reader.ReadInt32();
            pickups = new List<PickupIndex>(count);

            for (int i = 0; i < count; i++) 
            {
                pickups.Add(PickupIndex.ReadFromNetworkReader(reader));
            }
        }

        public void OnReceived() 
        {
            ChestItemsPlugin.Instance?.HandleShowItemPicker(targetId, pickups);
        }
    }
}