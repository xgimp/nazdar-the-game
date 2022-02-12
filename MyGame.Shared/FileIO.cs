﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.IsolatedStorage;
using System.Reflection;
using System.Text;

namespace MyGame
{
    public class FileIO
    {
        public string File { get; set; }
        private IsolatedStorageFile isoStore;
        private IsolatedStorageFileStream isoStream;

        public FileIO(string file = null)
        {
            this.File = file;
            this.isoStore = IsolatedStorageFile.GetStore(IsolatedStorageScope.User | IsolatedStorageScope.Assembly, null, null);
        }

        public object Load()
        {
            if (isoStore.FileExists(this.File))
            {
                this.isoStream = new IsolatedStorageFileStream(this.File, FileMode.Open, this.isoStore);
                using (this.isoStream)
                {
                    using (StreamReader reader = new StreamReader(isoStream))
                    {
                        string json = reader.ReadToEnd();
                        this.isoStream.Close();

                        try
                        {
                            dynamic data = JObject.Parse(json);
                            return data;
                        } 
                        catch
                        {
                            return null;
                        }
                    }
                }
            }

            return null;
        }

        public void Save(object data)
        {
            FileMode fm = this.isoStore.FileExists(this.File) ? FileMode.Truncate : FileMode.Create;
            this.isoStream = new IsolatedStorageFileStream(this.File, fm, this.isoStore);
            using (this.isoStream)
            {
                using (StreamWriter writer = new StreamWriter(isoStream))
                {
                    string json = JsonConvert.SerializeObject(data);
                    writer.WriteLine(json);
                    
                }
                this.isoStream.Close();
            }
        }

        public string GetPath()
        {
            return typeof(IsolatedStorageFileStream)
                .GetField("_fullPath", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(isoStream)
                .ToString();
        }
    }
}
