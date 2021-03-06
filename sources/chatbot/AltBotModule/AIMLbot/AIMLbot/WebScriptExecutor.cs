﻿using System;
using System.IO;
using MushDLR223.ScriptEngines;
using MushDLR223.Utilities;
using AltAIMLbot;

namespace AIMLbot
{
#if false
    public class WebScriptExecutor : ScriptExecutorGetter, ScriptExecutor
    {

        #region Implementation of ScriptExecutorGetter

        private AltBot TheBot;
        //private User myUser;

        public WebScriptExecutor(AltBot bot)
        {
            TheBot = bot;
        }
        public ScriptExecutor GetScriptExecuter(object o)
        {
            return this;
        }

        public void WriteLine(string s, params object[] args)
        {
            s = TextPatternUtils.SafeFormat(s, args);
            if (s.StartsWith("Trace")) return;
            if (s.StartsWith("Debug")) return;
            TheBot.writeToLog("HTTPD: " + s);
        }

        #endregion

        #region Implementation of ScriptExecutor

        public CmdResult ExecuteCommand(string s, object session, OutputDelegate outputDelegate)
        {
            StringWriter sw = new StringWriter();
            if (s == null) return new CmdResult("null cmd", false);
            s = s.Trim();
            if (s == "") return new CmdResult("empty cmd", false);
            if (s.StartsWith("aiml"))
            {
                s = s.Substring(4).Trim();
                if (s.StartsWith("@ "))
                    s = "@withuser" + s.Substring(1);
            }
            if (!s.StartsWith("@")) s = "@" + s;
            //     sw.WriteLine("AIMLTRACE " + s);
            User myUser = null;// TheBot.LastUser;
            //OutputDelegate del = outputDelegate ?? sw.WriteLine;
            bool r = TheBot.BotDirective(myUser, s, sw.WriteLine);
            sw.Flush();
            string res = sw.ToString();
            // for now legacy
            //res = res.Replace("menevalue=", "mene value=");
            if (outputDelegate != null) outputDelegate(res);
            WriteLine(res);
            return new CmdResult(res, r);
        }

        public CmdResult ExecuteXmlCommand(string s, object session, OutputDelegate outputDelegate)
        {
            return ExecuteCommand(s, session, outputDelegate);
        }

        public string GetName()
        {
            return TheBot.GlobalSettings.grabSettingNoDebug("NAME");
        }

        public object getPosterBoard(object slot)
        {
            string sslot = "" + slot;
            sslot = sslot.ToLower();
            var u = TheBot.GlobalSettings.grabSetting(sslot);
            if (Unifiable.IsNull(u)) return null;
            if (TextPatternUtils.IsNullOrEmpty(u)) return "";
            return u.ToValue(null);
        }

        #endregion
    }
#endif
}
