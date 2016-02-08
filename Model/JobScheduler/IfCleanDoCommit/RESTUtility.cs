﻿// -----------------------------------------------------------------------
// <copyright file="Utility.cs" company="CSIRO">
// TODO: Update copyright text.
// </copyright>
// -----------------------------------------------------------------------

namespace Utils
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Net;
    using System.IO;
    using System.Xml.Serialization;
    using System.Xml;

    /// <summary>
    /// TODO: Update summary.
    /// </summary>
    public class REST
    {
        /// <summary>Call REST web service.</summary>
        /// <typeparam name="T">The return type</typeparam>
        /// <param name="url">The URL of the REST service.</param>
        /// <returns>The return data</returns>
        public static T CallService<T>(string url)
        {
            WebRequest wrGETURL;
            wrGETURL = WebRequest.Create(url);
            wrGETURL.Method = "GET";
            wrGETURL.ContentType = @"application/xml; charset=utf-8";
            wrGETURL.ContentLength = 0;
            using (HttpWebResponse webresponse = wrGETURL.GetResponse() as HttpWebResponse)
            {
                Encoding enc = System.Text.Encoding.GetEncoding("utf-8");
                // read response stream from response object
                using (StreamReader loResponseStream = new StreamReader(webresponse.GetResponseStream(), enc))
                {
                    string st = loResponseStream.ReadToEnd();
                    if (typeof(T).Name == "Object" || st == null || st == string.Empty)
                        return default(T);

                    XmlSerializer serializer = new XmlSerializer(typeof(T));

                    //ResponseData responseData;
                    return (T)serializer.Deserialize(new NamespaceIgnorantXmlTextReader(new StringReader(st)));
                }
            }
        }

        /// <summary>Helper class to ignore namespaces when de-serializing</summary>
        public class NamespaceIgnorantXmlTextReader : XmlTextReader
        {
            /// <summary>Constructor</summary>
            /// <param name="reader">The text reader.</param>
            public NamespaceIgnorantXmlTextReader(System.IO.TextReader reader) : base(reader) { }

            /// <summary>Override the namespace.</summary>
            public override string NamespaceURI
            {
                get { return ""; }
            }
        }

        /// <summary>Return the valid password for this web service.</summary>
        public static string GetValidPassword()
        {
            string connectionString = File.ReadAllText(@"C:\inetpub\wwwroot\ChangeDBPassword.txt");
            int posPassword = connectionString.IndexOf("Password=");
            return connectionString.Substring(posPassword + "Password=".Length);
        }

    }
}
