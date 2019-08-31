using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Crestron.SimplSharp;
using Crestron.SimplSharp.Net.Https;
using Crestron.SimplSharp.Net.Http;
using Crestron.SimplSharp.Net;
using Crestron.SimplSharp.CrestronIO;

namespace LightwaveRfGen2
    {
        public static class LightwaveRfGen2Client
        {
            public delegate void LightwaveRfLoadChangedEventHandler(string room, string zone, uint channel, string type, uint value);
            public static Dictionary<string, LightwaveRfLoadChangedEventHandler> eventList = new Dictionary<string, LightwaveRfLoadChangedEventHandler>();

            public static HttpServer myServer;
            public static uint serverPort;
            public static string feedbackServerIp;
            public static ushort feedbackServerPort; 

            public static AuthRootObject authObj;
            public static StructuresMainRootObject structuresMainObj;
            public static StructuresAllRootObject structuresAllObj;
            public static List<RoomListRootObject> roomListObj;
            public static List<LightwaveRFEventListRootObject> eventsListObj;
            public static ZoneRootObject zoneListObj;
            public static Dictionary<string, List<string>> lightwaveRfLoadsSwitch = new Dictionary<string, List<string>>();
            public static Dictionary<string, List<string>> lightwaveRfLoadsDimLevel = new Dictionary<string, List<string>>();

            public static CTimer authRefreshTimer;

            public static string authUrl, authContent;

            public static ushort SimplSharpInitializer(string url, string content)
            {
                try
                {
                    // Initialize http server for feedback
                    myServer = new HttpServer();
                    myServer.Port = (int)serverPort;
                    myServer.ServerName = "0.0.0.0";
                    myServer.OnHttpRequest += new OnHttpRequestHandler(HttpCallbackFunction);
                    myServer.Active = true;
                    authUrl = url;
                    authContent = content;

                    lightwaveRfPostAuth(url, content); 
                    lightwaveRfGetStructures();
                    lightwaveRfGetStructuresAll();
                    lightwaveRfRoomList();
                    lightwaveRfZoneList();
                    lightwaveRfFillLoadsList();

                    foreach (var zone in zoneListObj.zone)
                    {
                        foreach (var room in LightwaveRfGen2Client.roomListObj)
                        {
                            eventList.Add((zone.name + "-" + room.name), (string r, string z, uint c, string t, uint v) => { });
                        }
                    }


                    if (authRefreshTimer != null)                                                                                            // Start timer only if was disposed or never started
                    {
                        if (authRefreshTimer.Disposed)
                            authRefreshTimer = new CTimer(delegate(object o) { lightwaveRfPostAuth(url, content); }, null, 60000, 36000000); // refresh every 10h
                    }
                    else
                        authRefreshTimer = new CTimer(delegate(object o) { lightwaveRfPostAuth(url, content); }, null, 60000, 36000000);

                    return 1;
                }
                catch (Exception e)
                {
                    ErrorLog.Error("Exception in SimplSharpInitializer: " + e.Message);
                    return 0;
                }
            }

            public static void SimplPlusClearEventList()
            {
                try
                {
                    lightwaveRfEventList();

                    foreach (var item in eventsListObj)
                    {
                        lightwaveRfDeleteEvent(item.id);
                    }
                }
                catch (Exception e)
                {
                    ErrorLog.Error("Exception in SimplPlusClearEventList: " + e.Message);
                }
            }
            
            internal static void SimplSharpEventSubscribe(string zoneName, string roomName, LightwaveRfLoadChangedEventHandler handler)
            {
                try
                {
                    foreach (var item in eventList)
                    {
                        if (item.Key == zoneName + "-" + roomName)
                        {
                            eventList[item.Key] += handler; 
                            break;
                        }
                    }
                }
                catch (Exception e)
                {
                    ErrorLog.Error("Exception in SimplSharpEventSubscribe : " + e.Message);
                }
            }

            internal static void SimplSharpEventUnsubscribe(string zoneName, string roomName, LightwaveRfLoadChangedEventHandler handler)
            {
                try
                {
                    foreach (var item in eventList)
                    {
                        if (item.Key == zoneName + "-" + roomName)
                        {
                            eventList[item.Key] -= handler;
                        }
                    }
                }
                catch (Exception e)
                {
                    ErrorLog.Error("Exception in SimplSharpEventUnsubscribe : " + e.Message);
                }
            }

            public static void HttpCallbackFunction(object sender, OnHttpRequestArgs e)
            {
                try
                {
                    if (e.Response.Code == 200)
                    {
                        WebHookRootObject responseObj = new WebHookRootObject();

                        CrestronConsole.PrintLine("Path = {0}", HttpUtility.UrlDecode(e.Request.Path));
                        CrestronConsole.PrintLine("Message = {0}", e.Request.ContentString);

                        if (e.Request.Path == "/endpoint")
                        {
                            responseObj = FileOperations.DeserializeJSON<WebHookRootObject>(responseObj, e.Request.ContentString);

                            lightwaveRfRoomLoadLevelFeedback(responseObj.triggerEvent.id, (uint)responseObj.payload.value);
                        }

                        e.Response.Header.SetHeaderValue("Content-Type", "text/html");
                        e.Response.ResponseText = "OK";
                        e.Response.Code = 200;
                    }
                    else
                    {
                        CrestronConsole.PrintLine(e.Response.ContentString);
                    }
                }
                catch (Exception exc)
                {
                    ErrorLog.Error("Error in HttpCallback: {0}", exc.Message);
                }
            }

            public static string ExtractString(string s, string Tag, string endTag)
            {
                var startTag = Tag;
                int startIndex = s.IndexOf(startTag) + startTag.Length;
                int endIndex = s.IndexOf(endTag, startIndex);
                return s.Substring(startIndex, endIndex - startIndex);
            }

            public static void lightwaveRfEventCreateSimplPlus(string zoneName, string roomName)
            {
                try
                {
                    LightwaveRFFeedbackCreateRootObject lightwaveRfCreateEventObj = new LightwaveRFFeedbackCreateRootObject();
                    LightwaveRFFeedbackUpdateRootObject lightwaveRfUpdateEventObj = new LightwaveRFFeedbackUpdateRootObject();

                    foreach (var zone in zoneListObj.zone)
                    {
                        if (zoneName == zone.name)
                        {
                            foreach (var room in roomListObj)
                            {
                                if (roomName == room.name)
                                {
                                    foreach (var device in structuresAllObj.devices)
                                    {
                                        foreach (var featureSets in device.featureSets)
                                        {
                                            foreach (var item in room.order)
                                            {
                                                if (item.ToString() == featureSets.featureSetId)
                                                {
                                                    foreach (var feature in featureSets.features)
                                                    {
                                                        if (feature.type == "switch" || feature.type == "dimLevel")
                                                        {
                                                            LightwaveRFFeedbackReadRootObject readEventObj = new LightwaveRFFeedbackReadRootObject();

                                                            lightwaveRfCreateEventObj.@ref = zoneName.Replace(" ", "_") + "-" + roomName.Replace(" ", "_") + "-" + featureSets.name.Replace(" ", "_") + "-" + feature.type;
                                                            lightwaveRfUpdateEventObj.@ref = zoneName.Replace(" ", "_") + "-" + roomName.Replace(" ", "_") + "-" + featureSets.name.Replace(" ", "_") + "-" + feature.type;
                                                            readEventObj = lightwaveRfGetEventRead(lightwaveRfCreateEventObj.@ref);
                                                            CrestronConsole.PrintLine("Read object ID : " + readEventObj.id);

                                                            if (readEventObj == null)
                                                            {
                                                                CrestronConsole.PrintLine("Object Doesn't Exist, Creating new one");
                                                                lightwaveRfCreateEventObj.url = "http://" + feedbackServerIp + ":" + feedbackServerPort + "/endpoint";
                                                                lightwaveRfCreateEventObj.events.Add(new LightwaveRFFeedbackCreateEvent() { type = "feature", id = feature.featureId });
                                                                lightwaveRfPatchEventCreate(lightwaveRfCreateEventObj);
                                                            }
                                                            else
                                                            {
                                                                CrestronConsole.PrintLine("Object Does Exist, Deleting");
                                                                lightwaveRfDeleteEvent(lightwaveRfUpdateEventObj.@ref); 
                                                                CrestronConsole.PrintLine("Then Adding");
                                                                lightwaveRfCreateEventObj.url = "http://" + feedbackServerIp + ":" + feedbackServerPort + "/endpoint";
                                                                lightwaveRfCreateEventObj.events.Add(new LightwaveRFFeedbackCreateEvent() { type = "feature", id = feature.featureId });
                                                                lightwaveRfPatchEventCreate(lightwaveRfCreateEventObj);
                                                                //CrestronConsole.PrintLine("Object Does Exist, Updating");               // PATCH request broken on Crestron..
                                                                //lightwaveRfUpdateEventObj.url = "http://" + feedbackServerIp + ":" + feedbackServerPort + "/endpoint/" + lightwaveRfUpdateEventObj.@ref;
                                                                //lightwaveRfUpdateEventObj.version = readEventObj.version + 1;
                                                                //lightwaveRfUpdateEventObj.events.Add(new LightwaveRFFeedbackUpdateEvent() { type = "feature", id = feature.featureId});
                                                                //lightwaveRfPatchEventUpdate(lightwaveRfUpdateEventObj.@ref, lightwaveRfUpdateEventObj);
                                                            }

                                                            lightwaveRfCreateEventObj = new LightwaveRFFeedbackCreateRootObject();
                                                            lightwaveRfUpdateEventObj = new LightwaveRFFeedbackUpdateRootObject();
                                                        }
                                                    }
                                                } 
                                            }
                                        }
                                    }
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (Exception exc)
                {
                    ErrorLog.Error("Exception in lightwaveRfPostEventCreate, Message : {0}", exc.Message);
                }
            }

            public static LightwaveRFFeedbackReadRootObject lightwaveRfGetEventRead(string eventId)
            {
                try
                {
                    HttpsClient Client = new HttpsClient();
                    HttpsClientResponse Response;
                    LightwaveRFFeedbackReadRootObject eventObj = new LightwaveRFFeedbackReadRootObject();

                    HttpsClientRequest Request;
                    Request = new HttpsClientRequest();
                    Request.ContentString = "";


                    HttpsHeader HeaderContentType = new HttpsHeader("Content-Type: application/json");
                    HttpsHeader HeaderAuthorization = new HttpsHeader("authorization: bearer " + authObj.tokens.access_token);

                    Request.Header.AddHeader(HeaderContentType); 
                    Request.Header.AddHeader(HeaderAuthorization);

                    Request.RequestType = Crestron.SimplSharp.Net.Https.RequestType.Get;

                    string url = "https://publicapi.lightwaverf.com/v1/events/" + eventId;

                    Client.TimeoutEnabled = true;
                    Client.Timeout = 5;
                    Client.KeepAlive = false;

                    CrestronConsole.PrintLine(url);
                    CrestronConsole.PrintLine(Request.ContentString);

                    Request.Url.Parse(url);
                    Response = Client.Dispatch(Request);

                    eventObj = FileOperations.DeserializeJSON<LightwaveRFFeedbackReadRootObject>(eventObj, Response.ContentString);

                    return eventObj;

                }
                catch (Exception exc)
                {
                    ErrorLog.Error("Exception in lightwaveRfPostStructures, Message : {0}", exc.Message);
                    return null;
                }
            }

            public static void lightwaveRfPatchEventUpdate(string eventId, LightwaveRFFeedbackUpdateRootObject eventObj)
            {
                try
                {
                    

                    HttpsHeader HeaderContentType = new HttpsHeader("Content-Type: application/json");
                    HttpsHeader HeaderAuthorization = new HttpsHeader("authorization: bearer " + authObj.tokens.access_token);

                    string url = "https://publicapi.lightwaverf.com/v1/events/" + eventId;

                    using (var httpClient = new HttpsClient
                    {
                        KeepAlive = false,
                        TimeoutEnabled = true,
                        Timeout = 3,
                        PeerVerification = false,
                        HostVerification = false,
                        Verbose = true
                    })
                    {

                        var request = new HttpsClientRequest { RequestType = Crestron.SimplSharp.Net.Https.RequestType.Patch };

                        request.ContentString = FileOperations.SerializeJSON<LightwaveRFFeedbackUpdateRootObject>(eventObj);
                        request.Header.AddHeader(HeaderContentType);
                        request.Header.AddHeader(HeaderAuthorization);

                        request.Url.Parse(string.Format(url));
                        request.FinalizeHeader();

                        var response = httpClient.Dispatch(request);
                        CrestronConsole.PrintLine("HTTP Response Code: {0}\n{1}", response.Code, response.ContentString);
                    }

                }
                catch (Exception exc)
                {
                    ErrorLog.Error("Exception in lightwaveRfPatchEventUpdate, Message : {0}", exc.Message);
                }
            }

            public static void lightwaveRfPatchEventCreate(LightwaveRFFeedbackCreateRootObject eventObj)
            {
                try
                {
                    HttpsClient Client = new HttpsClient();
                    HttpsClientResponse Response;

                    HttpsClientRequest Request;
                    Request = new HttpsClientRequest();
                    Request.ContentString = FileOperations.SerializeJSON<LightwaveRFFeedbackCreateRootObject>(eventObj);


                    HttpsHeader HeaderContentType = new HttpsHeader("Content-Type: application/json");
                    HttpsHeader HeaderAuthorization = new HttpsHeader("authorization: bearer " + authObj.tokens.access_token);

                    Request.Header.AddHeader(HeaderContentType);
                    Request.Header.AddHeader(HeaderAuthorization);

                    Request.RequestType = Crestron.SimplSharp.Net.Https.RequestType.Post;

                    string url = "https://publicapi.lightwaverf.com/v1/events";

                    Client.TimeoutEnabled = true;
                    Client.Timeout = 5;
                    Client.KeepAlive = false;

                    CrestronConsole.PrintLine(url);
                    CrestronConsole.PrintLine(Request.ContentString);

                    Request.Url.Parse(url);
                    Response = Client.Dispatch(Request);

                }
                catch (Exception exc)
                {
                    ErrorLog.Error("Exception in lightwaveRfPatchEventCreate, Message : {0}", exc.Message);
                }
            }

            public static void lightwaveRfDeleteEvent(string eventId)
            {
                try
                {
                    HttpsClient Client = new HttpsClient();
                    HttpsClientResponse Response;

                    HttpsClientRequest Request;
                    Request = new HttpsClientRequest();
                    Request.ContentString = "";


                    HttpsHeader HeaderContentType = new HttpsHeader("Content-Type: application/json");
                    HttpsHeader HeaderAuthorization = new HttpsHeader("authorization: bearer " + authObj.tokens.access_token);

                    Request.Header.AddHeader(HeaderContentType);
                    Request.Header.AddHeader(HeaderAuthorization);

                    Request.RequestType = Crestron.SimplSharp.Net.Https.RequestType.Delete;

                    string url = "https://publicapi.lightwaverf.com/v1/events/" + eventId;

                    Client.TimeoutEnabled = true;
                    Client.Timeout = 5;
                    Client.KeepAlive = false;

                    CrestronConsole.PrintLine(url);
                    CrestronConsole.PrintLine(Request.ContentString);

                    Request.Url.Parse(url);
                    Response = Client.Dispatch(Request);

                }
                catch (Exception exc)
                {
                    ErrorLog.Error("Exception in lightwaveRfPatchEventCreate, Message : {0}", exc.Message);
                }
            }

            public static string lightwaveRfPostAuth(string url_string, string content_string)
            {
                try
                {
                    HttpsClient Client = new HttpsClient();
                    HttpsClientResponse Response;

                    HttpsClientRequest Request;
                    Request = new HttpsClientRequest();
                    Request.ContentString = content_string;


                    HttpsHeader HeaderContentType = new HttpsHeader("Content-Type: application/json");
                    HttpsHeader HeaderCacheControl = new HttpsHeader("cache-control: no-cache");
                    HttpsHeader HeaderAppid = new HttpsHeader("x-lwrf-appid: ios-01");

                    Request.Header.AddHeader(HeaderContentType);
                    Request.Header.AddHeader(HeaderCacheControl);
                    Request.Header.AddHeader(HeaderAppid);


                    Request.RequestType = Crestron.SimplSharp.Net.Https.RequestType.Post;

                    string url = url_string;

                    Client.TimeoutEnabled = true;
                    Client.Timeout = 5;
                    Client.KeepAlive = false;

                    Request.Url.Parse(url);
                    Response = Client.Dispatch(Request);


                    authObj = FileOperations.DeserializeJSON<AuthRootObject>(authObj, Response.ContentString);

                    CrestronConsole.PrintLine(authObj.tokens.access_token);

                    return "LightwaveRF Authorized!";
                }
                catch (Exception exc)
                {
                    ErrorLog.Error("Exception in lightwaveRfPostAuth, Message : {0}", exc.Message);
                    return "LightwaveRF Error, check logs";
                }
            }

            public static void lightwaveRfGetStructures()
            {
                try
                {
                    HttpsClient Client = new HttpsClient();
                    HttpsClientResponse Response;

                    HttpsClientRequest Request;
                    Request = new HttpsClientRequest();
                    Request.ContentString = "";


                    HttpsHeader HeaderContentType = new HttpsHeader("Content-Type: application/json");
                    HttpsHeader HeaderAuthorization = new HttpsHeader("authorization: bearer " + authObj.tokens.access_token);

                    Request.Header.AddHeader(HeaderContentType);
                    Request.Header.AddHeader(HeaderAuthorization);

                    Request.RequestType = Crestron.SimplSharp.Net.Https.RequestType.Get;

                    string url = "https://publicapi.lightwaverf.com/v1/structures";

                    Client.TimeoutEnabled = true;
                    Client.Timeout = 5;
                    Client.KeepAlive = false;

                    Request.Url.Parse(url);
                    Response = Client.Dispatch(Request);
                    
                    structuresMainObj = FileOperations.DeserializeJSON<StructuresMainRootObject>(structuresMainObj, Response.ContentString);

                }
                catch (Exception exc)
                {
                    ErrorLog.Error("Exception in lightwaveRfPostStructures, Message : {0}", exc.Message);
                }
            }

            public static void lightwaveRfGetStructuresAll()
            {
                try
                {
                    HttpsClient Client = new HttpsClient();
                    HttpsClientResponse Response;

                    HttpsClientRequest Request;
                    Request = new HttpsClientRequest();
                    Request.ContentString = "";


                    HttpsHeader HeaderContentType = new HttpsHeader("Content-Type: application/json");
                    HttpsHeader HeaderAuthorization = new HttpsHeader("authorization: bearer " + authObj.tokens.access_token);

                    Request.Header.AddHeader(HeaderContentType);
                    Request.Header.AddHeader(HeaderAuthorization);

                    Request.RequestType = Crestron.SimplSharp.Net.Https.RequestType.Get;

                    string url = "https://publicapi.lightwaverf.com/v1/structure/" + structuresMainObj.structures[0] + "?";

                    Client.TimeoutEnabled = true;
                    Client.Timeout = 5;
                    Client.KeepAlive = false;

                    Request.Url.Parse(url);
                    Response = Client.Dispatch(Request);

                    structuresAllObj = FileOperations.DeserializeJSON<StructuresAllRootObject>(structuresAllObj, Response.ContentString);

                }
                catch (Exception exc)
                {
                    ErrorLog.Error("Exception in lightwaveRfPostStructuresAll, Message : {0}", exc.Message);
                }
            }

            public static void lightwaveRfEventList()
            {
                try
                {
                    HttpsClient Client = new HttpsClient();
                    HttpsClientResponse Response;

                    HttpsClientRequest Request;
                    Request = new HttpsClientRequest();
                    Request.ContentString = "";


                    HttpsHeader HeaderContentType = new HttpsHeader("Content-Type: application/json");
                    HttpsHeader HeaderAuthorization = new HttpsHeader("authorization: bearer " + authObj.tokens.access_token);

                    Request.Header.AddHeader(HeaderContentType);
                    Request.Header.AddHeader(HeaderAuthorization);

                    Request.RequestType = Crestron.SimplSharp.Net.Https.RequestType.Get;

                    string url = "https://publicapi.lightwaverf.com/v1/events";

                    Client.TimeoutEnabled = true;
                    Client.Timeout = 5;
                    Client.KeepAlive = false;

                    Request.Url.Parse(url);
                    Response = Client.Dispatch(Request);

                    eventsListObj = FileOperations.DeserializeJSON<List<LightwaveRFEventListRootObject>>(eventsListObj, Response.ContentString);
                }
                catch (Exception exc)
                {
                    ErrorLog.Error("Exception in lightwaveRfEventList, Message : {0}", exc.Message);
                }
            }

            public static void lightwaveRfRoomList()
            {
                try
                {
                    HttpsClient Client = new HttpsClient();
                    HttpsClientResponse Response;

                    HttpsClientRequest Request;
                    Request = new HttpsClientRequest();
                    Request.ContentString = "";


                    HttpsHeader HeaderContentType = new HttpsHeader("Content-Type: application/json");
                    HttpsHeader HeaderAuthorization = new HttpsHeader("authorization: bearer " + authObj.tokens.access_token);

                    Request.Header.AddHeader(HeaderContentType);
                    Request.Header.AddHeader(HeaderAuthorization);

                    Request.RequestType = Crestron.SimplSharp.Net.Https.RequestType.Get;

                    string url = "https://publicapi.lightwaverf.com/v1/rooms";

                    Client.TimeoutEnabled = true;
                    Client.Timeout = 5;
                    Client.KeepAlive = false;

                    Request.Url.Parse(url);
                    Response = Client.Dispatch(Request);

                    roomListObj = FileOperations.DeserializeJSON<List<RoomListRootObject>>(roomListObj, Response.ContentString);


                    ///

                    

                }
                catch (Exception exc)
                {
                    ErrorLog.Error("Exception in lightwaveRfRoomList, Message : {0}", exc.Message);
                }
            }

            public static void lightwaveRfZoneList()
            {
                try
                {
                    HttpsClient Client = new HttpsClient();
                    HttpsClientResponse Response;

                    HttpsClientRequest Request;
                    Request = new HttpsClientRequest();
                    Request.ContentString = "";


                    HttpsHeader HeaderContentType = new HttpsHeader("Content-Type: application/json");
                    HttpsHeader HeaderAuthorization = new HttpsHeader("authorization: bearer " + authObj.tokens.access_token);

                    Request.Header.AddHeader(HeaderContentType);
                    Request.Header.AddHeader(HeaderAuthorization);

                    Request.RequestType = Crestron.SimplSharp.Net.Https.RequestType.Get;

                    string url = "https://publicapi.lightwaverf.com/v1/zones";

                    Client.TimeoutEnabled = true;
                    Client.Timeout = 5;
                    Client.KeepAlive = false;

                    Request.Url.Parse(url);
                    Response = Client.Dispatch(Request);

                    zoneListObj = FileOperations.DeserializeJSON<ZoneRootObject>(zoneListObj, Response.ContentString);

                }
                catch (Exception exc)
                {
                    ErrorLog.Error("Exception in lightwaveRfPostStructures, Message : {0}", exc.Message);
                }
            }

            public static void lightwaveRfFillLoadsList()
            {
                try
                {
                    foreach (var room in roomListObj)
                    {
                        List<string> buffSwitch = new List<string>();
                        List<string> buffDimLevel = new List<string>();
                        string zoneBuff = "";

                        foreach (var zone in zoneListObj.zone)
                        {
                            if (room.parentGroups[0] == zone.groupId)
                            {
                                zoneBuff = zone.name;
                            }
                        }

                        foreach (var featuresID in room.order)
                        {
                            foreach (var device in structuresAllObj.devices)
                            {
                                foreach (var featureSet in device.featureSets)
                                {
                                    bool isDimmable = false;
                                    foreach (var feature in featureSet.features)
                                    {
                                        if (featuresID.ToString() == featureSet.featureSetId)
                                        {
                                            if (feature.type == "switch")
                                            {
                                                buffSwitch.Add(feature.featureId);
                                                foreach (var featureSearch in featureSet.features)
                                                {
                                                    if (featureSearch.type == "dimLevel")
                                                    {
                                                        isDimmable = true;
                                                        buffDimLevel.Add(featureSearch.featureId);
                                                        break;
                                                    }
                                                }
                                                if (!isDimmable)
                                                {
                                                    buffDimLevel.Add("");
                                                }
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        lightwaveRfLoadsSwitch.Add(zoneBuff + "-" + room.name, buffSwitch);
                        lightwaveRfLoadsDimLevel.Add(zoneBuff + "-" + room.name, buffDimLevel);
                    }
                }
                catch (Exception e)
                {
                    ErrorLog.Error("Exception in lightwaveRfFillLoadsList: " + e.Message);
                }
            }

            public static void lightwaveRfRoomAllOn(string roomName, string zoneName)
            {
                try
                {
                    FeatureBatchRootObject batch = new FeatureBatchRootObject();
                    string url = "https://publicapi.lightwaverf.com/v1/features/write";

                    HttpsClient Client = new HttpsClient();
                    HttpsClientResponse Response;

                    HttpsClientRequest Request;
                    Request = new HttpsClientRequest();

                    foreach (var zone in zoneListObj.zone)
                    {
                        if (zoneName == zone.name)
                        {
                            foreach (var room in roomListObj)
                            {
                                if (roomName == room.name)
                                {
                                    foreach (var device in structuresAllObj.devices)
                                    {
                                        foreach (var featureSets in device.featureSets)
                                        {
                                            foreach (var item in room.order)
                                            {
                                                if (item.ToString() == featureSets.featureSetId)
                                                {
                                                    uint i = 0;
                                                    foreach (var feature in featureSets.features)
                                                    {
                                                        if (feature.type == "switch")
                                                        {
                                                            batch.features.Add(new FeatureBatch() { featureId = feature.featureId, value = 1 });
                                                            //loadChangedEvent(roomName, zoneName, i+1, feature.type, 1);
                                                            i++;
                                                        }
                                                        else if (feature.type == "dimLevel")
                                                        {
                                                            //LightwaveRfFeatureReadRootObject value = lightwaveRfReadFeatureValue(feature.featureId);
                                                            //loadChangedEvent(roomName, zoneName, i + 1, feature.type, (uint)value.value);
                                                        }
                                                    }
                                                }  
                                            }
                                        }
                                    }
                                    break;
                                }
                            }
                        }
                    }

                    Request.ContentString = FileOperations.SerializeJSON<FeatureBatchRootObject>(batch);

                    CrestronConsole.PrintLine(Request.ContentString);


                    HttpsHeader HeaderContentType = new HttpsHeader("Content-Type: application/json");
                    HttpsHeader HeaderAuthorization = new HttpsHeader("authorization: bearer " + authObj.tokens.access_token);

                    Request.Header.AddHeader(HeaderContentType);
                    Request.Header.AddHeader(HeaderAuthorization);

                    Request.RequestType = Crestron.SimplSharp.Net.Https.RequestType.Post;

                    Client.TimeoutEnabled = true;
                    Client.Timeout = 5;
                    Client.KeepAlive = false;

                    Request.Url.Parse(url);
                    Response = Client.Dispatch(Request);

                }
                catch (Exception exc)
                {
                    ErrorLog.Error("Exception in lightwaveRfRoomAllOn, Message : {0}", exc.Message);
                }
            }                        

            public static void lightwaveRfRoomAllOff(string roomName, string zoneName)
            {
                try
                {
                    FeatureBatchRootObject batch = new FeatureBatchRootObject();
                    string url = "https://publicapi.lightwaverf.com/v1/features/write";

                    HttpsClient Client = new HttpsClient();
                    HttpsClientResponse Response;

                    HttpsClientRequest Request;
                    Request = new HttpsClientRequest();

                    foreach (var zone in zoneListObj.zone)
                    {
                        if (zoneName == zone.name)
                        {
                            foreach (var room in roomListObj)
                            {
                                if (roomName == room.name)
                                {
                                    foreach (var device in structuresAllObj.devices)
                                    {
                                        foreach (var featureSets in device.featureSets)
                                        {
                                            foreach (var item in room.order)
                                            {
                                                if (item.ToString() == featureSets.featureSetId)
                                                {
                                                    uint i = 0;
                                                    foreach (var feature in featureSets.features)
                                                    {
                                                        if (feature.type == "switch")
                                                        {
                                                            batch.features.Add(new FeatureBatch() { featureId = feature.featureId, value = 0 });
                                                            break;
                                                        }
                                                        i++;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    break;
                                }
                            }
                        }
                    }

                    Request.ContentString = FileOperations.SerializeJSON<FeatureBatchRootObject>(batch);

                    CrestronConsole.PrintLine(Request.ContentString);


                    HttpsHeader HeaderContentType = new HttpsHeader("Content-Type: application/json");
                    HttpsHeader HeaderAuthorization = new HttpsHeader("authorization: bearer " + authObj.tokens.access_token);

                    Request.Header.AddHeader(HeaderContentType);
                    Request.Header.AddHeader(HeaderAuthorization);

                    Request.RequestType = Crestron.SimplSharp.Net.Https.RequestType.Post;

                    Client.TimeoutEnabled = true;
                    Client.Timeout = 5;
                    Client.KeepAlive = false;

                    Request.Url.Parse(url);
                    Response = Client.Dispatch(Request);

                }
                catch (Exception exc)
                {
                    ErrorLog.Error("Exception in lightwaveRfRoomAllOn, Message : {0}", exc.Message);
                }
            }                       

            public static LightwaveRfFeatureReadRootObject lightwaveRfReadFeatureValue(string featureId)
            {
                try
                {
                    LightwaveRfFeatureReadRootObject featureObj = new LightwaveRfFeatureReadRootObject();
                    string url = "https://publicapi.lightwaverf.com/v1/feature/" + featureId + "?";

                    HttpsClient Client = new HttpsClient();
                    HttpsClientResponse Response;

                    HttpsClientRequest Request;
                    Request = new HttpsClientRequest();

                    HttpsHeader HeaderContentType = new HttpsHeader("Content-Type: application/json");
                    HttpsHeader HeaderAuthorization = new HttpsHeader("authorization: bearer " + authObj.tokens.access_token);

                    Request.Header.AddHeader(HeaderContentType);
                    Request.Header.AddHeader(HeaderAuthorization);

                    Request.RequestType = Crestron.SimplSharp.Net.Https.RequestType.Get;

                    Client.TimeoutEnabled = true;
                    Client.Timeout = 5;
                    Client.KeepAlive = false;
                    CrestronConsole.PrintLine("Sending Request!");
                    Request.Url.Parse(url);
                    Response = Client.Dispatch(Request);
                    CrestronConsole.PrintLine("Response.ContentString: "+ Response.ContentString);
                    featureObj = FileOperations.DeserializeJSON<LightwaveRfFeatureReadRootObject>(featureObj, Response.ContentString);
                    CrestronConsole.PrintLine("Request sent! " + featureObj.featureId);
                    return featureObj;

                }
                catch (Exception exc)
                {
                    ErrorLog.Error("Exception in lightwaveRfReadFeatureValue, Message : {0}", exc.Message);
                    return null;
                }
            } 

            public static void lightwaveRfRoomLoadLevel(string roomName, string zoneName, uint loadNum, uint loadLevel)
            {
                try
                {
                    string url = String.Empty;

                    HttpsClient Client = new HttpsClient();
                    HttpsClientResponse Response;

                    HttpsClientRequest Request;
                    Request = new HttpsClientRequest();
                    if (loadLevel <= 100)
                        Request.ContentString = "{\"value\": " + loadLevel + "}";
                    else
                        ErrorLog.Error("Error : Value must be 0 to 100");


                    HttpsHeader HeaderContentType = new HttpsHeader("Content-Type: application/json");
                    HttpsHeader HeaderAuthorization = new HttpsHeader("authorization: bearer " + authObj.tokens.access_token);

                    Request.Header.AddHeader(HeaderContentType);
                    Request.Header.AddHeader(HeaderAuthorization);

                    Request.RequestType = Crestron.SimplSharp.Net.Https.RequestType.Post;

                    #region OldLogic
                    /*foreach (var zone in zoneListObj.zone)
                    {
                        if (zoneName == zone.name)
                        {
                            foreach (var item in roomListObj)
                            {
                                if (roomName == item.name)
                                {
                                    foreach (var subItem in structuresAllObj.devices)
                                    {
                                        foreach (var subsubItem in subItem.featureSets)
                                        {
                                            if (item.order[(int)loadNum].ToString() == subsubItem.featureSetId)
                                            {
                                                foreach (var subsubsubItem in subsubItem.features)
                                                {
                                                    if (subsubsubItem.type == "dimLevel")
                                                    {

                                                        url = "https://publicapi.lightwaverf.com/v1/feature/" + subsubsubItem.featureId + "?";
                                                        break;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    break;
                                }
                            }
                        }
                    }*/
                    #endregion

                    List<string> dictValue = lightwaveRfLoadsDimLevel.TryGetValue(zoneName + "-" + roomName, out dictValue) ? dictValue : null;
                    if (dictValue != null)
                    {
                        if (loadNum < dictValue.Count)
                        {
                            url = "https://publicapi.lightwaverf.com/v1/feature/" + dictValue[(int)loadNum] + "?";
                        }
                        else
                            return;
                    }
                    else
                        return;

                    CrestronConsole.PrintLine("url = " + url);


                    Client.TimeoutEnabled = true;
                    Client.Timeout = 5;
                    Client.KeepAlive = false;

                    Request.Url.Parse(url);
                    Response = Client.Dispatch(Request);

                    if (loadLevel > 0)
                        lightwaveRfRoomLoadOn(roomName, zoneName, loadNum);
                    else
                        lightwaveRfRoomLoadOff(roomName, zoneName, loadNum);

                }
                catch (Exception exc)
                {
                    ErrorLog.Error("Exception in lightwaveRfRoomLoadLevel, Message : {0}", exc.Message);
                }
            }
            
            public static void lightwaveRfRoomLoadLevelFeedback(string featureId, uint loadLevel)
            { 
                try
                {
                    foreach (var device in structuresAllObj.devices)
                        {
                            foreach (var featureSets in device.featureSets)
                            {
                                foreach (var feature in featureSets.features)
                                {
                                    if (feature.featureId == featureId)
                                    {
                                        foreach (var zone in zoneListObj.zone)
                                        {
                                            foreach (var room in roomListObj)
                                            {
                                                uint i = 0;
                                                if (zone.groupId == room.parentGroups[0])
                                                {
                                                    foreach (var item in room.order)
                                                    {
                                                        if (item.ToString() == featureSets.featureSetId)
                                                        {
                                                            if (feature.type == "switch")
                                                            { 
                                                                if (loadLevel > 0)
                                                                {
                                                                    eventList[zone.name + "-" + room.name].Invoke(room.name, zone.name, i + 1, feature.type, loadLevel);
                                                                    if (lightwaveRfGetLoadDimmability(room.name, zone.name, i) == 0)
                                                                    {
                                                                        eventList[zone.name + "-" + room.name].Invoke(room.name, zone.name, i + 1, "dimLevel", 100);
                                                                    }
                                                                    else
                                                                    {
                                                                        foreach (var searchFeature in featureSets.features)
                                                                        {
                                                                            if (searchFeature.type == "dimLevel")
                                                                            {
                                                                                LightwaveRfFeatureReadRootObject value = lightwaveRfReadFeatureValue(searchFeature.featureId);
                                                                                eventList[zone.name + "-" + room.name].Invoke(room.name, zone.name, i + 1, "dimLevel", (uint)value.value);
                                                                            }
                                                                        }
                                                                    }

                                                                }
                                                                else
                                                                {
                                                                    eventList[zone.name + "-" + room.name].Invoke(room.name, zone.name, i + 1, feature.type, loadLevel);
                                                                    eventList[zone.name + "-" + room.name].Invoke(room.name, zone.name, i + 1, "dimLevel", 0);
                                                                }
                                                            }
                                                            else if (feature.type == "dimLevel")
                                                            {
                                                                eventList[zone.name + "-" + room.name].Invoke(room.name, zone.name, i + 1, feature.type, loadLevel);
                                                            }
                                                            break;
                                                        }
                                                        i++;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }

                }
                catch (Exception exc)
                {
                    ErrorLog.Error("Exception in lightwaveRfRoomLoadLevelFeedback, Message : {0}", exc.Message);
                }
            }

            public static void lightwaveRfGetLoadInfo(string roomName, string zoneName)
            {
                try
                {

                    List<string> dictValue = lightwaveRfLoadsSwitch.TryGetValue(zoneName + "-" + roomName, out dictValue) ? dictValue : null;

                    uint i = 0;

                    if (dictValue != null)
                    {
                        foreach (var item in dictValue)
                        {
                            LightwaveRfFeatureReadRootObject value = lightwaveRfReadFeatureValue(item);
                            if (value.value > 0)
                            {
                                if (lightwaveRfGetLoadDimmability(roomName, zoneName, i) == 0)
                                {
                                    eventList[zoneName + "-" + roomName].Invoke(roomName, zoneName, i + 1, "dimLevel", 100);
                                }
                                else
                                {
                                    LightwaveRfFeatureReadRootObject dimValue = lightwaveRfReadFeatureValue(lightwaveRfLoadsDimLevel[zoneName + "-" + roomName][(int)i]);
                                    eventList[zoneName + "-" + roomName].Invoke(roomName, zoneName, i + 1, "dimLevel", (uint)dimValue.value);
                                }
                            }
                            else
                            {
                                if (lightwaveRfGetLoadDimmability(roomName, zoneName, i) == 0)
                                {
                                    eventList[zoneName + "-" + roomName].Invoke(roomName, zoneName, i + 1, "dimLevel", 0);
                                }
                            }
                            eventList[zoneName + "-" + roomName].Invoke(roomName, zoneName, i + 1, "switch", (uint)value.value);
                            i++;
                        }
                    }
                }
                catch (Exception e)
                {
                    ErrorLog.Error("Exception in lightwaveRfGetLoadInfo, Message : {0}", e.Message);
                }
            }

            public static void lightwaveRfGetLoadValue(string roomName, string zoneName, ushort loadNum)
            {
                try
                {
                    List<string> dictValue = lightwaveRfLoadsDimLevel.TryGetValue(zoneName + "-" + roomName, out dictValue) ? dictValue : null;
                    if (dictValue != null)
                    {
                        if (loadNum < dictValue.Count)
                        {
                            LightwaveRfFeatureReadRootObject value = lightwaveRfReadFeatureValue(dictValue[(int)loadNum]);
                            eventList[zoneName + "-" + roomName].Invoke(roomName, zoneName, (uint)loadNum, "dimLevel", (uint)value.value);
                        }
                        else
                            return;
                    }
                    else
                        return;

                }
                catch (Exception e)
                {
                    ErrorLog.Error("Exception in lightwaveRfGetLoadValue, Message : {0}", e.Message);
                }
            }

            public static string lightwaveRfGetLoadName(string roomName, string zoneName, uint loadNum)
            {
                string returnValue = "";

                try
                {
                    foreach (var zone in zoneListObj.zone)
                    {
                        if (zoneName == zone.name)
                        {
                            foreach (var item in roomListObj)
                            {
                                if (roomName == item.name)
                                {
                                    foreach (var subItem in structuresAllObj.devices)
                                    {
                                        foreach (var subsubItem in subItem.featureSets)
                                        {
                                            foreach (var feature in subsubItem.features)
                                            {
                                                if (feature.type == "switch")
                                                {
                                                    List<string> dictValue = lightwaveRfLoadsSwitch.TryGetValue(zoneName + "-" + roomName, out dictValue) ? dictValue : null;
                                                    if(dictValue != null)
                                                    {
                                                        if (loadNum < dictValue.Count)
                                                        {
                                                            if (dictValue[(int)loadNum] == feature.featureId)
                                                            {
                                                                returnValue = subsubItem.name;
                                                                break;
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    break;
                                }
                            }
                        }
                    }

                    return returnValue;
                }
                catch (Exception e)
                {
                    ErrorLog.Error("Exception in lightwaveRfGetLoadName, Message : {0}", e.Message);
                    return "Exception in lightwaveRfGetLoadName, Message :" + e.Message;
                }
            }

            public static ushort lightwaveRfGetLoadDimmability(string roomName, string zoneName, uint loadNum)
            {
                try
                {
                    List<string> dictValue = lightwaveRfLoadsDimLevel.TryGetValue(zoneName + "-" + roomName, out dictValue) ? dictValue : null;
                    if (dictValue != null)
                    {
                        if (loadNum < dictValue.Count)
                        {
                            if (dictValue[(int)loadNum].Length > 0)
                                return 1;
                            else
                                return 0;
                        }
                    }

                    return 0;
                }
                catch (Exception e)
                {
                    ErrorLog.Error("Exception in lightwaveRfGetLoadDimmability, Message : {0}", e.Message);
                    return 0;
                }
            }

            public static void lightwaveRfRoomLoadOn(string roomName, string zoneName, uint loadNum)
            {
                try
                {
                    string url = String.Empty;

                    HttpsClient Client = new HttpsClient();
                    HttpsClientResponse Response;

                    HttpsClientRequest Request;
                    Request = new HttpsClientRequest();
                    Request.ContentString = "{\"value\": 1}";


                    HttpsHeader HeaderContentType = new HttpsHeader("Content-Type: application/json");
                    HttpsHeader HeaderAuthorization = new HttpsHeader("authorization: bearer " + authObj.tokens.access_token);

                    Request.Header.AddHeader(HeaderContentType);
                    Request.Header.AddHeader(HeaderAuthorization);

                    Request.RequestType = Crestron.SimplSharp.Net.Https.RequestType.Post;
                    #region OldLogic
                    /*
                    foreach (var zone in zoneListObj.zone)
                    {
                        if (zoneName == zone.name)
                        {
                            foreach (var item in roomListObj)
                            {
                                if (roomName == item.name)
                                {
                                    foreach (var subItem in structuresAllObj.devices)
                                    {
                                        foreach (var subsubItem in subItem.featureSets)
                                        {
                                            if (item.order[(int)loadNum].ToString() == subsubItem.featureSetId)
                                            {
                                                foreach (var subsubsubItem in subsubItem.features)
                                                {
                                                    if (subsubsubItem.type == "switch")
                                                    {
                                                        url = "https://publicapi.lightwaverf.com/v1/feature/" + subsubsubItem.featureId + "?";
                                                    }
                                                    else if (subsubsubItem.type == "dimLevel")
                                                    {
                                                        //LightwaveRfFeatureReadRootObject value = lightwaveRfReadFeatureValue(subsubsubItem.featureId);
                                                        //loadChangedEvent(roomName, zoneName, loadNum, subsubsubItem.type, (uint)value.value);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    break;
                                }
                            }
                        }
                    }
                     * */
                    #endregion

                    List<string> dictValue = lightwaveRfLoadsSwitch.TryGetValue(zoneName + "-" + roomName, out dictValue) ? dictValue : null;
                    if (dictValue != null)
                    {
                        if (loadNum < dictValue.Count)
                        {
                            url = "https://publicapi.lightwaverf.com/v1/feature/" + dictValue[(int)loadNum]+ "?";
                        }
                        else
                            return;
                    }
                    else
                        return;

                    Client.TimeoutEnabled = true;
                    Client.Timeout = 5;
                    Client.KeepAlive = false;

                    Request.Url.Parse(url);
                    Response = Client.Dispatch(Request);

                }
                catch (Exception exc)
                {
                    ErrorLog.Error("Exception in lightwaveRfRoomLoadOn, Message : {0}", exc.Message);
                }
            }

            public static void lightwaveRfRoomLoadOff(string roomName, string zoneName, uint loadNum)
            {
                try
                {
                    string url = String.Empty;

                    HttpsClient Client = new HttpsClient();
                    HttpsClientResponse Response;

                    HttpsClientRequest Request;
                    Request = new HttpsClientRequest();
                    Request.ContentString = "{\"value\": 0}";


                    HttpsHeader HeaderContentType = new HttpsHeader("Content-Type: application/json");
                    HttpsHeader HeaderAuthorization = new HttpsHeader("authorization: bearer " + authObj.tokens.access_token);

                    Request.Header.AddHeader(HeaderContentType);
                    Request.Header.AddHeader(HeaderAuthorization);

                    Request.RequestType = Crestron.SimplSharp.Net.Https.RequestType.Post;

                    #region OldLogic
                    /*
                    foreach (var zone in zoneListObj.zone)
                    {
                        if (zoneName == zone.name)
                        {
                            foreach (var item in roomListObj)
                            {
                                if (roomName == item.name)
                                {
                                    foreach (var subItem in structuresAllObj.devices)
                                    {
                                        foreach (var subsubItem in subItem.featureSets)
                                        {
                                            if (item.order[(int)loadNum].ToString() == subsubItem.featureSetId)
                                            {
                                                foreach (var subsubsubItem in subsubItem.features)
                                                {
                                                    if (subsubsubItem.type == "switch")
                                                    {
                                                        url = "https://publicapi.lightwaverf.com/v1/feature/" + subsubsubItem.featureId + "?";
                                                    }
                                                    else if (subsubsubItem.type == "dimLevel")
                                                    {
                                                        //loadChangedEvent(roomName, zoneName, loadNum, subsubsubItem.type, 0);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    break;
                                }
                            }
                        }
                    }
                     * */
                    #endregion

                    List<string> dictValue = lightwaveRfLoadsSwitch.TryGetValue(zoneName + "-" + roomName, out dictValue) ? dictValue : null;
                    if (dictValue != null)
                    {
                        if (loadNum < dictValue.Count)
                        {
                            url = "https://publicapi.lightwaverf.com/v1/feature/" + dictValue[(int)loadNum] + "?";
                        }
                        else
                            return;
                    }
                    else
                        return;

                    Client.TimeoutEnabled = true;
                    Client.Timeout = 5;
                    Client.KeepAlive = false;

                    Request.Url.Parse(url);
                    Response = Client.Dispatch(Request);

                }
                catch (Exception exc)
                {
                    ErrorLog.Error("Exception in lightwaveRfRoomLoadOff, Message : {0}", exc.Message);
                }
            }

            public static ushort CalculateLoadLevel(ushort inputMin, ushort inputMax, ushort outputMin, ushort outputMax, ushort value)          
            {
                try
                {
                    double ain = value;
                    double InputLowerLimit = inputMin;
                    double InputUpperLimit = inputMax;
                    double OutputLowerLimit = outputMin;
                    double OutputUpperLimit = outputMax;
                    double I;

                    if (ain <= InputLowerLimit)
                        return Convert.ToUInt16(OutputLowerLimit);
                    if (ain >= InputUpperLimit)
                        return Convert.ToUInt16(OutputUpperLimit);
                    if (ain > InputLowerLimit && ain < InputUpperLimit)
                    {
                        I = ((ain - InputLowerLimit) / (InputUpperLimit - InputLowerLimit)) * (OutputUpperLimit - OutputLowerLimit);

                        if (inputMax < outputMax)
                        {
                            return Convert.ToUInt16(InputLowerLimit + Math.Ceiling(I)); 
                        }
                        else
                        {
                            double scale = (double)(outputMax - outputMin) / (inputMax - inputMin);
                            return (ushort)(outputMin + ((ain - inputMin) * scale));
                        }
                    }

                    return 0;
                }
                catch (Exception e)
                {
                    ErrorLog.Error("Error in CalculateLoadLevel: {0}", e.Message);
                    return 0;
                }
            }

            public static ushort ConvertRange(ushort originalStart, ushort originalEnd, ushort newStart, ushort newEnd, ushort value)
            {
                try
                {
                    double scale = (double)(newEnd - newStart) / (originalEnd - originalStart);
                    return (ushort)(newStart + ((value - originalStart) * scale));
                }
                catch (Exception e)
                {
                    ErrorLog.Error("Exception in ConvertRange, Message : {0}", e.Message);
                    return 0;
                }
                
            }

            public static double Round(double d)
            {
                var absoluteValue = Math.Abs(d);
                var integralPart = (long)absoluteValue;
                var decimalPart = absoluteValue - integralPart;
                var sign = Math.Sign(d);

                double roundedNumber;

                if (decimalPart > 0.5)
                {
                    roundedNumber = integralPart + 1;
                }
                else if (decimalPart == 0)
                {
                    roundedNumber = absoluteValue;
                }
                else
                {
                    roundedNumber = integralPart + 0.5;
                }

                return sign * roundedNumber;
            }    

        }

        #region LightwaveRFAuthJson
        public class AuthMyClient
        {
            public string bearer { get; set; }
            public string refresh_token { get; set; }
        }

        public class AuthLightwaveRfPublic
        {
            public int user_id { get; set; }
            public int active { get; set; }
            public int active_campaign_id { get; set; }
            public int t_and_c_consent { get; set; }
            public AuthMyClient myClient { get; set; }
        }

        public class AuthProviders
        {
        }

        public class AuthUser
        {
            public string givenName { get; set; }
            public string email { get; set; }
            public AuthLightwaveRfPublic lightwaveRfPublic { get; set; }
            public AuthProviders providers { get; set; }
            public string _id { get; set; }
            public long created { get; set; }
            public long modified { get; set; }
        }

        public class AuthTokens
        {
            public string access_token { get; set; }
            public string token_type { get; set; }
            public int expires_in { get; set; }
            public string refresh_token { get; set; }
            public string id_token { get; set; }
        }

        public class AuthRootObject
        {
            public AuthUser user { get; set; }
            public AuthTokens tokens { get; set; }
        }
        #endregion
        #region LightwaveRFStructuresMainObj
        public class StructuresMainRootObject
        {
            public List<string> structures { get; set; }
        }
        #endregion        
        #region LightwaveRFStructuresAllObj
        public class StructuresAllFeature
        {
            public string featureId { get; set; }
            public string type { get; set; }
            public bool writable { get; set; }
        }

        public class StructuresAllFeatureSet
        {
            public string featureSetId { get; set; }
            public string name { get; set; }
            public List<StructuresAllFeature> features { get; set; }
        }

        public class StructuresAllDevice
        {
            public string deviceId { get; set; }
            public string name { get; set; }
            public string productCode { get; set; }
            public List<StructuresAllFeatureSet> featureSets { get; set; }
            public string product { get; set; }
            public string device { get; set; }
            public string desc { get; set; }
            public string type { get; set; }
            public string cat { get; set; }
            public int gen { get; set; }
        }

        public class StructuresAllRootObject
        {
            public string name { get; set; }
            public string groupId { get; set; }
            public List<StructuresAllDevice> devices { get; set; }
        }
        #endregion
        #region LightwaveRFRoomListObj
        public class RoomListRootObject
        {
            public string groupId { get; set; }
            public string name { get; set; }
            public string type { get; set; }
            public List<string> parentGroups { get; set; }
            public bool visible { get; set; }
            public List<object> order { get; set; }
            public List<object> featureSets { get; set; }
            public List<object> scriptSets { get; set; }
            public List<object> automationSets { get; set; }
        }
        #endregion
        #region LightwaveRFZoneListObj
        public class Zone
        {
            public string groupId { get; set; }
            public string name { get; set; }
            public string type { get; set; }
            public List<string> parentGroups { get; set; }
            public bool visible { get; set; }
            public List<string> order { get; set; }
            public List<string> rooms { get; set; }
        }

        public class ZoneRootObject
        {
            public List<Zone> zone { get; set; }
        }
        #endregion
        #region LightwaveRFFeatureBatch
        public class FeatureBatch
        {
            public string featureId;
            public int value;
        }

        public class FeatureBatchRootObject
        {
            public List<FeatureBatch> features = new List<FeatureBatch>();
        }
        #endregion
        #region LightwaveRFFeedbackCreateObj
        public class LightwaveRFFeedbackCreateEvent
        {
            public string type;
            public string id;
        }
        public class LightwaveRFFeedbackCreateRootObject
        {
            public List<LightwaveRFFeedbackCreateEvent> events = new List<LightwaveRFFeedbackCreateEvent>();
            public string url;
            public string @ref;
        }
        #endregion
        #region LightwaveRFFeedbackUpdateObj
        public class LightwaveRFFeedbackUpdateEvent
        {
            public string type;
            public string id;
        }
        public class LightwaveRFFeedbackUpdateRootObject
        {
            public List<LightwaveRFFeedbackUpdateEvent> events = new List<LightwaveRFFeedbackUpdateEvent>();
            public string url;
            public string @ref;
            public int version;
        }
        #endregion
        #region LightwaveRFFeedbackReadObj
        public class LightwaveRFFeedbackReadEvent
        {
            public string type { get; set; }
            public string id { get; set; }
        }

        public class LightwaveRFFeedbackReadMeta
        {
            public List<LightwaveRFFeedbackReadEvent> events { get; set; }
            public string url { get; set; }
            public string @ref { get; set; }
            public string userId { get; set; }
            public string clientId { get; set; }
        }

        public class LightwaveRFFeedbackReadRootObject
        {
            public bool active { get; set; }
            public int version { get; set; }
            public string userId { get; set; }
            public string clientId { get; set; }
            public string id { get; set; }
            public string type { get; set; }
            public DateTime createdAt { get; set; }
            public DateTime updatedAt { get; set; }
            public LightwaveRFFeedbackReadMeta meta { get; set; }
        }
        #endregion
        #region LightwaveRFEventListRootObject
        public class LightwaveRFEventListRootObject
        {
            public string id { get; set; }
            public string type { get; set; }
            public int version { get; set; }
            public bool active { get; set; }
            public string userId { get; set; }
        }
        #endregion
        #region LightwaveRFWebhookObj
        public class WebHookTriggerEvent
        {
            public string type { get; set; }
            public string id { get; set; }
        }

        public class WebHookEvent
        {
            public string type { get; set; }
            public string id { get; set; }
        }

        public class WebHookPayload
        {
            public long time { get; set; }
            public int value { get; set; }
        }

        public class WebHookRootObject
        {
            public string id { get; set; }
            public string userId { get; set; }
            public WebHookTriggerEvent triggerEvent { get; set; }
            public List<WebHookEvent> events { get; set; }
            public WebHookPayload payload { get; set; }
        }
        #endregion
        #region LightwaveRfFeatureReadObj
        public class LightwaveRfFeatureReadRootObject
        {
            public string featureId { get; set; }
            public int value { get; set; }
        }
        #endregion
        #region LightwaveRfFeatureBatchReadObj
        #endregion
        #region HelpObject
        public class HelpObject
        {
            public string roomName;
            public string zoneName;
        }
        #endregion


        public class LightwaveRfEventHandlerClass
        {
            public delegate void LightwaveRfLoadChangedEventHandler(object o, MyLightwaveRfEventArgs args);
            public event LightwaveRfLoadChangedEventHandler myEvent = delegate { };

            public string zone;
            public string room;


            MyLightwaveRfEventArgs args = new MyLightwaveRfEventArgs();


            public LightwaveRfEventHandlerClass() 
            {

            }

            public void subscribeEvents(string zoneName, string roomName)
            {
                this.zone = zoneName;
                this.room = roomName;
                LightwaveRfGen2Client.SimplSharpEventSubscribe(zoneName, roomName, LightwaveRfGen2Client_loadChangedEvent);
            }

            void LightwaveRfGen2Client_loadChangedEvent(string room, string zone, uint channel, string type, uint value)
            {
                args.room = room;
                args.zone = zone;
                args.channel = (ushort)channel;
                args.type = type;
                args.value = (ushort)value;

                if(this.zone == zone && this.room == room)
                    myEvent(this, args);
            }
        }

        public class MyLightwaveRfEventArgs : EventArgs
        {
            public ushort channel;
            public ushort value;
            public string type;
            public string room;
            public string zone;

            public MyLightwaveRfEventArgs()
            {

            }
        }
    }