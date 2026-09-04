using Photon.Pun;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace NoLeaves
{
    public static class CustomProperty
    {
        public const string ID = "github.com/poopooVR/NoLeaves";

        public static void SetCustomNetworkProperty()
        {
            if (PhotonNetwork.InRoom)
            {
                Hashtable customProps = new Hashtable();
                customProps.Add(ID, "true");
                PhotonNetwork.LocalPlayer.SetCustomProperties(customProps);
            }
        }

        public static void RemoveCustomNetworkProperty()
        {
            if (PhotonNetwork.InRoom)
            {
                Hashtable customProps = new Hashtable();
                customProps.Add(ID, null);
                PhotonNetwork.LocalPlayer.SetCustomProperties(customProps);
            }
        }
    }
}
