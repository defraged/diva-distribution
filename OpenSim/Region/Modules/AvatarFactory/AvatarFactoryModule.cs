/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSim Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.Reflection;

using System.Threading;
using libsecondlife;
using log4net;
using Nini.Config;
using OpenSim.Data.Base;
using OpenSim.Data.MapperFactory;
using OpenSim.Framework;
using OpenSim.Framework.Communications.Cache;
using OpenSim.Region.Environment.Interfaces;
using OpenSim.Region.Environment.Scenes;

namespace OpenSim.Region.Modules.AvatarFactory
{
    public class AvatarFactoryModule : IAvatarFactory, IRegionModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private Scene m_scene = null;
        private static readonly AvatarAppearance def = new AvatarAppearance();

        public bool TryGetAvatarAppearance(LLUUID avatarId, out AvatarAppearance appearance)
        {
            CachedUserInfo profile = m_scene.CommsManager.UserProfileCacheService.GetUserDetails(avatarId);
            if ((profile != null) && (profile.RootFolder != null)) 
            {
                appearance = m_scene.CommsManager.UserService.GetUserAppearance(avatarId);
                if (appearance != null) 
                {
                    SetAppearanceAssets(profile, ref appearance);
                    m_log.InfoFormat("[APPEARANCE] found : {0}", appearance.ToString());
                    return true;
                }
            }

            appearance = CreateDefault(avatarId);
            m_log.InfoFormat("[APPEARANCE] appearance not found for {0}, creating default", avatarId.ToString());
            return false;
        }

        private AvatarAppearance CreateDefault(LLUUID avatarId)
        {
            AvatarAppearance appearance = null;
            AvatarWearable[] wearables;
            byte[] visualParams;
            GetDefaultAvatarAppearance(out wearables, out visualParams);
            appearance = new AvatarAppearance(avatarId, wearables, visualParams);

            return appearance;
        }

        public void Initialise(Scene scene, IConfigSource source)
        {
            scene.RegisterModuleInterface<IAvatarFactory>(this);
            scene.EventManager.OnNewClient += NewClient;

            if (m_scene == null)
            {
                m_scene = scene;
            }

        }

        public void PostInitialise()
        {
        }

        public void Close()
        {
        }

        public string Name
        {
            get { return "Default Avatar Factory"; }
        }

        public bool IsSharedModule
        {
            get { return true; }
        }

        public void NewClient(IClientAPI client)
        {
            client.OnAvatarNowWearing += AvatarIsWearing;
        }

        public void RemoveClient(IClientAPI client)
        {
            // client.OnAvatarNowWearing -= AvatarIsWearing;
        }


        public void SetAppearanceAssets(CachedUserInfo profile, ref AvatarAppearance appearance)
        {
            if (profile.RootFolder != null)
            {
                for (int i = 0; i < 13; i++)
                {
                    if (appearance.Wearables[i].ItemID == LLUUID.Zero)
                    {
                        appearance.Wearables[i].AssetID = LLUUID.Zero;
                    }
                    else
                    {
                        LLUUID assetId;

                        InventoryItemBase baseItem = profile.RootFolder.FindItem(appearance.Wearables[i].ItemID);

                        if (baseItem != null)
                        {
                            appearance.Wearables[i].AssetID = baseItem.AssetID;
                        }
                        else
                        {
                            m_log.ErrorFormat("[APPEARANCE] Can't find inventory item {0}, setting to default", appearance.Wearables[i].ItemID);
                            appearance.Wearables[i].AssetID = def.Wearables[i].AssetID;
                        }
                    }
                }
            }
            else
            {
                m_log.Error("[APPEARANCE] you have no inventory, appearance stuff isn't going to work");
            }
        }

        public void AvatarIsWearing(Object sender, AvatarWearingArgs e)
        {
            IClientAPI clientView = (IClientAPI)sender;
            ScenePresence avatar = m_scene.GetScenePresence(clientView.AgentId);
            if(avatar == null) {
                m_log.Info("Avatar is child agent, ignoring AvatarIsWearing event");
                return;
            }

            CachedUserInfo profile = m_scene.CommsManager.UserProfileCacheService.GetUserDetails(clientView.AgentId);

            AvatarAppearance avatAppearance = null;
            if(!TryGetAvatarAppearance(clientView.AgentId, out avatAppearance)) {
                m_log.Info("We didn't seem to find the appearance, falling back to ScenePresense");
                avatAppearance = avatar.Appearance;
            }
            m_log.Info("Calling Avatar is Wearing");
            if (profile != null)
            {
                if (profile.RootFolder != null)
                {
                    foreach (AvatarWearingArgs.Wearable wear in e.NowWearing)
                    {
                        if (wear.Type < 13)
                        {
                            avatAppearance.Wearables[wear.Type].ItemID = wear.ItemID;
                        }
                    }
                    SetAppearanceAssets(profile, ref avatAppearance);
                    
                    m_scene.CommsManager.UserService.UpdateUserAppearance(clientView.AgentId, avatAppearance);
                    avatar.Appearance = avatAppearance;                    
                }
                else
                {
                    m_log.Error("Root Profile is null, we can't set the appearance");
                }
            }
        }

        public static void GetDefaultAvatarAppearance(out AvatarWearable[] wearables, out byte[] visualParams)
        {
            visualParams = GetDefaultVisualParams();
            wearables = AvatarWearable.DefaultWearables;
        }

        public void UpdateDatabase(LLUUID user, AvatarAppearance appearance)
        { 
            m_scene.CommsManager.UserService.UpdateUserAppearance(user, appearance);
        }

        private static byte[] GetDefaultVisualParams()
        {
            byte[] visualParams;
            visualParams = new byte[218];
            for (int i = 0; i < 218; i++)
            {
                visualParams[i] = 100;
            }
            return visualParams;
        }
    }
}
