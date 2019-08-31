using System;
using System.Text;
using System.Text.RegularExpressions;
using Crestron.SimplSharp;                          	// For Basic SIMPL# Classes
using Newtonsoft.Json;                                  // For JSON Class
using Crestron.SimplSharp.Net.Http;
using Crestron.SimplSharp.CrestronIO; 

namespace LightwaveRfGen2
{
    public static class FileOperations
    {
        private static FileStream myStream;
        private static StreamReader myReader;

        #region Local Files
        public static string OpenLocalFile(String strPath)
        {
            string buff = "";

            try
            {
                myStream = new FileStream(strPath, FileMode.Open);
            }
            catch (FileNotFoundException e)
            {
                ErrorLog.Error("FileNotFoundException: {0}", e.Message);
            }
            catch (IOException e)
            {
                ErrorLog.Error("IOException: {0}", e.Message);
            }
            catch (DirectoryNotFoundException e)
            {
                ErrorLog.Error("DirectoryNotFoundException: {0}", e.Message);
            }
            catch (PathTooLongException e)
            {
                ErrorLog.Error("PathTooLongException: {0}", e.Message);
            }
            catch (Exception e)
            {
                ErrorLog.Error("Exception: {0}", e.Message);
            }

            try
            {
                myReader = new StreamReader(myStream);
                while (!myReader.EndOfStream)
                {
                    buff += myReader.ReadLine();
                }
            }
            catch (Exception e)
            {
                ErrorLog.Error("Exception: {0}", e.Message);
            }
            finally
            {
                myStream.Close();
            }

            return buff;
        }
        public static void SaveLocalFile(String strPath, String toSave, FileMode fileMode)
        {
            try
            {
                if (File.Exists(strPath))
                {
                    myStream = new FileStream(strPath, fileMode);
                    myStream.Write(toSave, Encoding.ASCII);
                }
                else
                {
                    myStream = new FileStream(strPath, FileMode.Create);
                    myStream.Write(toSave, Encoding.ASCII);
                }
            }
            catch (Exception e)
            {
                ErrorLog.Error("FileNotFoundException : {0}", e.Message);
            }
            finally
            {
                myStream.Close();
            }

        }
        #endregion
        #region JSON
        public static T DeserializeJSON<T>(T obj, string data)
        {
            return JsonConvert.DeserializeObject<T>(data);
        }
        public static string SerializeJSON<T>(T[] data)
        {
            return JsonConvert.SerializeObject(data, Formatting.Indented);
        }
        public static string SerializeJSON<T>(T data)
        {
            return JsonConvert.SerializeObject(data, Formatting.Indented);
        }
        #endregion
    }
}