﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace bantam_php
{
    class Helper
    {
        /// <summary>
        /// 
        /// </summary>
        private static Random rdm = new Random();

        /// <summary>
        /// 
        /// </summary>
        /// <param name="maxLength"></param>
        /// <param name="matchLength"></param>
        /// <returns></returns>
        public static int RandomNumber(int maxNumber)
        {
            return rdm.Next(1, maxNumber);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="maxLength"></param>
        /// <param name="matchLength"></param>
        /// <returns></returns>
        public static string RandomNumberString(int maxLength, bool matchLength = true)
        {
            string s = string.Empty;

            if (!matchLength) {
                maxLength = rdm.Next(1, maxLength);
            }

            for (int i = 0; i < maxLength; i++) {
                s += rdm.Next(10).ToString();
            }

            return s;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="length"></param>
        /// <returns></returns>
        public static string RandomString(int length, bool capitals = true, bool numbers = false)
        {
            var charSet = "abcdefghijklmnopqrstuvwxyz";

            if (capitals) {
                charSet += "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            }

            if (numbers) {
                charSet += "0123456789";
            }

            var stringResult = new char[length];

            for (int i = 0; i < length; i++) {
                stringResult[i] = charSet[rdm.Next(charSet.Length)];
            }
            return new string(stringResult);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="base64"></param>
        /// <returns></returns>
        public static string EncodeBase64Tostring(string base64)
        {
            if (string.IsNullOrEmpty(base64)) {
                return String.Empty;
            }

            var plainTextBytes = Encoding.UTF8.GetBytes(base64);
            string b64Code = Convert.ToBase64String(plainTextBytes);
            return b64Code;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        public static byte[] DecodeBase64(string str)
        {
            if (string.IsNullOrEmpty(str)) {
                return null;
            }

            if (Regex.IsMatch(str, @"^[a-zA-Z0-9\+/]*={0,2}$")) {
                string cleanB64 = Regex.Replace(str, "[^a-zA-Z0-9+=/]", "");
                var decbuff = Convert.FromBase64String(cleanB64);
                return decbuff;
            } else {
                MessageBox.Show(str, "Unable to decode base64!");
                return null;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="code"></param>
        /// <returns></returns>
        public static string MinifyCode(string code)
        {
            string clean = Regex.Replace(code, @"\t|\n|\r", string.Empty);
            string clean2 = Regex.Replace(clean, @"[^\u0000-\u007F]+", string.Empty);
            return Regex.Replace(clean2, @"\s+", " ");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="TKey"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="dict"></param>
        /// <returns></returns>
        public static TKey RandomDicionaryValue<TKey, TValue>(Dictionary<TKey, TValue> dict)
        {
            Random rand = new Random();
            List<TKey> keyList = new List<TKey>(dict.Keys);

            return keyList[rand.Next(keyList.Count)];
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns></returns>
        public static string FormatBytes(double bytes)
        {
            int i = 0;
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };

            for (; i < suffixes.Length && bytes >= 1024; i++, bytes /= 1024) { }

            return String.Format("{0:0.##} {1}", bytes, suffixes[i]);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="list"></param>
        public static void ShuffleList<T>(IList<T> list)
        {
            Random random = new Random();
            int n = list.Count;

            for (int i = list.Count - 1; i > 1; i--) {
                int rnd = random.Next(i + 1);

                T value = list[rnd];
                list[rnd] = list[i];
                list[i] = value;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="uri"></param>
        /// <returns></returns>
        public static bool IsValidUri(string uri)
        {
            bool uriResult = Uri.TryCreate(uri, UriKind.Absolute, out Uri tempUri);
            return uriResult && (tempUri.Scheme == Uri.UriSchemeHttp || tempUri.Scheme == Uri.UriSchemeHttps);
        }
    }
}
