using DXP;
using SCH;
using System;

namespace Altium17PartSearch
{
    internal static class AltiumApi
    {
        internal static class GlobalVars
        {
            internal static IClient Client => DXP.GlobalVars.Client;
            private static ISch_ServerInterface _server;
            internal static ISch_ServerInterface SCHServer
            {
                get
                {
                    if (_server == null)
                    {
                        Client.StartServer("SCH");
                        _server = Client.GetServerModuleByName("SCH") as ISch_ServerInterface
                            ?? throw new InvalidOperationException("Cannot instantiate the schematic server.");
                    }
                    return _server;
                }
            }
        }
    }
    internal static class EESCH
    {
        internal static ISch_Lib GetCurrentSchLibrary() => AltiumApi.GlobalVars.SCHServer.GetCurrentSchDocument() as ISch_Lib;
        internal static void AddParameter(ISch_Component component, string name, string value)
        {
            var parameter = component.AddSchParameter();
            parameter.SetState_Text(value);
            parameter.SetState_Name(name);
            parameter.SetState_ShowName(false);
            parameter.SetState_IsHidden(true);
        }
    }
}
