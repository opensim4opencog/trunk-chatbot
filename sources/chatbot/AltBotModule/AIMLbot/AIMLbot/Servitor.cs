﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using DcBus;

using System.Threading;
using Aima.Core.Logic.Propositional.Algorithms;
using Aima.Core.Logic.Propositional.Parsing;
using Aima.Core.Logic.Propositional.Parsing.AST;
/******************************************************************************************
AltAIMLBot -- Copyright (c) 2011-2012,Kino Courssey, Daxtron Labs

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute,
sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or
substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT
NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT
OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
**************************************************************************************************/

namespace AltAIMLbot
{
    public class Servitor
    {
        // Connects a AltBot with some world, real or virtual
        // Creates multiple threads so it can have an independent existance

        // To be used with RTPBot
        // see 
        //   - Bot.cs RTPBot(),
        //   - BotConsole.cs Prepare(), 
        //   - WorldObjectsForAimLBot.cs StartupListener00()

        public  AltBot curBot;
        public  User curUser;
        public  Thread tmTalkThread = null;
        public  Thread tmFSMThread = null;
        public  Thread tmBehaveThread = null;

        public  Thread myCronThread = null;
        public  string lastAIMLInstance = "";
        public bool traceServitor = true;
        public Servitor(string UserID, sayProcessorDelegate outputDelegate)
        {
            Start(UserID, outputDelegate);
        }
        public void Start(string UserID,sayProcessorDelegate outputDelegate)
        {
            Console.WriteLine("RealBot operating in :" + Environment.CurrentDirectory);
            Console.WriteLine("       ProcessorCount:" + Environment.ProcessorCount);
            Console.WriteLine("             UserName:" + Environment.UserName);
            Console.WriteLine("            TickCount:" + Environment.TickCount);
            AltBot myBot = new AltBot();
            myBot.bbSafe = true;

            curBot = myBot;
            if (outputDelegate == null)
            {
                myBot.sayProcessor = new sayProcessorDelegate(sayResponse);
            }
            else
            {
                myBot.sayProcessor = outputDelegate;
            }
            myBot.loadSettings();
            User myUser = new User(UserID, myBot);

            curUser = myUser;
            myBot.isAcceptingUserInput = false;
            startMtalkWatcher();
            Thread.Sleep(600);
            bool personDefined = checkNewPersonality();
            lock (myBot)
            {
                if ((personDefined == false) && (lastAIMLInstance.Length == 0))
                {
                    //myBot.loadAIMLFromFiles();
                }
            }
            myBot.isAcceptingUserInput = true;

            startFSMEngine();
            startBehaviorEngine();
            startCronEngine();

        }

        public string respondToChat(string input)
        {
            try
            {
                    Request r = new Request(input, curUser, curBot);
                    Result res = curBot.Chat(r);
                    if (traceServitor)
                    {
                        Console.WriteLine("SERVITOR: respondToChat({0})={1}", input, res.Output);
                    }
                    return res.Output;
            }
            catch
            { return "..."; }

        }
        public string respondToChat(string input,string UserID)
        {
            try
            {
                User u = new User(UserID, curBot);
                Request r = new Request(input, u, curBot);
                Result res = curBot.Chat(r);
                if (traceServitor)
                {
                    Console.WriteLine("SERVITOR: respondToChat({0},{2})={1}", input, res.Output, UserID);
                }
                return res.Output;
            }
            catch
            { return "..."; }

        }

        public void reactToChat(string input)
        {
            try
            {
                Request r = new Request(input, curUser, curBot);
                Result res = curBot.Chat(r);
                if (traceServitor)
                {
                    Console.WriteLine("SERVITOR: reactToChat({0})={1}", input, res.Output);
                }
                sayResponse(res.Output);
            }
            catch
            { }

        }
        public void reactToChat(string input, string UserID)
        {
            try
            {
                User u = new User(UserID, curBot);
                Request r = new Request(input, u, curBot);
                Result res = curBot.Chat(r);
                if (traceServitor)
                {
                    Console.WriteLine("SERVITOR: reactToChat({0},{2})={1}", input, res.Output, UserID);
                }
                sayResponse(res.Output);
            }
            catch
            { }

        }

        public void Main(string[] args)
        {
            Start("consoleUser", new sayProcessorDelegate(sayResponse));

            while (true)
            {
                try
                {
                    Console.Write("You: ");
                    string input = Console.ReadLine();
                    if (input.ToLower() == "quit")
                    {
                        break;
                    }
                    else
                    {
                        string answer = respondToChat(input);
                        Console.WriteLine("Bot: " + answer);
                        sayResponse(answer);
                    }
                }
                catch
                { }

            }
        }


        public  void startCronEngine()
        {
            try
            {
                if (myCronThread == null)
                {
                    myCronThread = new Thread(curBot.myCron.start);
                }
                myCronThread.Name = "cron";
                myCronThread.IsBackground = true;
                myCronThread.Start();
            }
            catch (Exception e)
            {
            }
        }
        #region FSM
        public  void startFSMEngine()
        {
            //Start our own chem thread
            try
            {
                if (tmFSMThread == null)
                {
                    tmFSMThread = new Thread(memFSMThread);
                }
                tmFSMThread.IsBackground = true;
                tmFSMThread.Start();
            }
            catch (Exception e)
            {
            }

        }

        public void memFSMThread()
        {
            int interval = 1000;
            int tickrate = interval;
            while (true)
            {
                try
                {
                    if ((curBot.myFSMS != null) && (curBot.isAcceptingUserInput))
                    {
                        curBot.myFSMS.runBotMachines(curBot);
                    }
                    Thread.Sleep(interval);
                    string tickrateStr = getBBHash("tickrate");
                    tickrate = interval;

                    tickrate = int.Parse(tickrateStr);
                }
                catch
                {
                    tickrate = interval;
                }
                interval = tickrate;
            }

        }
        #endregion
       #region behavior
        public  void startBehaviorEngine()
        {
            //Start our own chem thread
            try
            {
                if (tmBehaveThread == null)
                {
                    tmBehaveThread = new Thread(memBehaviorThread);
                }
                tmBehaveThread.IsBackground = true;
                tmBehaveThread.Start();
            }
            catch (Exception e)
            {
            }

        }

        public  void memBehaviorThread()
        {
            int interval = 1000;
            int tickrate = interval;
            while (true)
            {
                try
                {
                    
                    if ((curBot.myBehaviors != null) && (curBot.isAcceptingUserInput))
                    {
                        try
                        {
                            curBot.myBehaviors.runBotBehaviors(curBot);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine("ERR: {0}\n{1}", e.Message, e.StackTrace);
                        }
                    }
                    Thread.Sleep(interval);
                    string tickrateStr = getBBHash("tickrate");
                    tickrate = interval;

                    tickrate = int.Parse(tickrateStr);
                }
                catch
                {
                    tickrate = interval;
                }
                interval = tickrate;
            }

        }
        #endregion




        public  void startMtalkWatcher()
        {
            if (curBot.myChemistry == null)
            {
                curBot.realChem = new Qchem(myConst.MEMHOST);
                curBot.myChemistry = new RChem(myConst.MEMHOST, true);
            }
            //Start our own chem thread
            try
            {
                if (tmTalkThread == null)
                {
                    tmTalkThread = new Thread(memTalkThread);
                }
                tmTalkThread.IsBackground = true;
                tmTalkThread.Start();
            }
            catch (Exception e)
            {
            }
        }
        public  bool checkNewPersonality()
        {
            bool loadedCore = false;

            lock (curBot)
            {
                if (safeBB())
                {
                    try
                    {
                        string curAIMLClass = getBBHash("aimlclassdir");
                        string curAIMLInstance = getBBHash("aimlinstancedir");
                        if (!(lastAIMLInstance.Contains(curAIMLInstance)))
                        {
                            Console.WriteLine("loadAIMLFromFiles: " + curAIMLClass);
                            Console.WriteLine("loadAIMLFromFiles: " + curAIMLInstance);
                            curBot.isAcceptingUserInput = false;
                            //myCronThread.Abort(); //.Suspend();
                            //tmFSMThread.Abort(); //.Suspend();
                            //tmBehaveThread.Abort(); //.Suspend();

                            lastAIMLInstance = curAIMLInstance;
                            if (curBot.myCron != null) curBot.myCron.clear();
                            curBot.Size = 0;
                            curBot.loadAIMLFromFiles();
                            loadedCore = true;
                            if (Directory.Exists(curAIMLClass)) curBot.loadAIMLFromFiles(curAIMLClass);
                            if (Directory.Exists(curAIMLInstance)) curBot.loadAIMLFromFiles(curAIMLInstance);

                            Console.WriteLine("Load Complete.");

                            curBot.isAcceptingUserInput = true;
                            //startMtalkWatcher();
                            //startFSMEngine();
                            //startBehaviorEngine();
                            //startCronEngine();

                            //myCronThread.Resume();
                            //tmFSMThread.Resume();
                            //tmBehaveThread.Resume();

                            return loadedCore;
                        }
                    }
                    catch (Exception e)
                    {
                        curBot.isAcceptingUserInput = true;
                        Console.WriteLine("Warning:*** Load Incomplete. *** \n {0}\n{1}", e.Message, e.StackTrace);
                    }

                }
                return loadedCore;
            }
        }

        public  void memTalkThread()
        {
            int interval = 200;
            int lastuutid = 0;
            int uutid = 0;
            //string utterance = "";

            string lastUtterance = "";
            Console.WriteLine("");
            Console.WriteLine("******* MTALK ACTIVE *******");
            Console.WriteLine("");

            while (true)
            {

                if (safeBB())
                {
                   bool newPerson= checkNewPersonality();
                    

                    string sv = null;
                    try
                    {
                        //sv = myChemistry.m_cBus.getHash("mdollhearduuid");
                        sv = getBBHash("uttid");
                        uutid = int.Parse(sv);
                    }
                    catch (Exception e) { }
                    if (uutid == lastuutid) { continue; }

                    try
                    {
                        lastuutid = uutid;
                        //string myInput = (myChemistry.m_cBus.getHash("mdollheard"));
                        string myInput = (getBBHash("speechhyp"));

                        //Get lastTTS output as <that>
                        string myThat = (getBBHash("TTSText"));
                        curUser.blackBoardThat = myThat;
                        //Get fsmstate output as <state>
                        string myState = (getBBHash("fsmstate"));
                        curUser.Predicates.updateSetting("state",myState);

                        // get values off the blackboard
                        curBot.importBBBot();
                        curBot.importBBUser(curUser);

                        if ((myInput.Length > 0) && (!myInput.Equals(lastUtterance)))
                        {
                            Console.WriteLine("Heard: " + myInput);
                            Request r = new Request(myInput, curUser, curBot);
                            Result res = curBot.Chat(r);
                            string myResp = res.Output;
                            Console.WriteLine("Response: " + myResp);
                            if (myResp == null)
                            {
                                myResp = "I don't know how to respond.";
                            }
                            myResp = myResp.Replace("_", " ");
                            Console.WriteLine("*** AIMLOUT = '{0}'", myResp);
                            if (!myResp.ToUpper().Contains("IGNORENOP"))
                            {
                                sayResponse(myResp);
                                setBBHash("lsaprior", myInput);
                            }

                            lastUtterance = myInput;
                        }
                    }
                    catch (Exception e) { }


                }
                updateTime();
                Thread.Sleep(interval);
            }
        }

        public  void updateTime()
        {
            setBBHash("userdate", DateTime.Now.Date.ToString());
            setBBHash("useryear", DateTime.Now.Year.ToString());
            setBBHash("usermonth", DateTime.Now.Month.ToString());
            setBBHash("userday", DateTime.Now.Day.ToString());
            setBBHash("userdayofweek", DateTime.Now.DayOfWeek.ToString());
            setBBHash("usertimeofday", DateTime.Now.TimeOfDay.ToString());
            setBBHash("userhhour", DateTime.Now.Hour.ToString());
            setBBHash("userminute", DateTime.Now.Minute.ToString());
        }

        public bool safeBB()
        {
            //bbSafe=(curBot.myChemistry != null) && (curBot.myChemistry.m_cBus != null);
            return curBot.bbSafe;
        }
        public void setBBHash(string key, string data)
        {
            //curBot.myChemistry.m_cBus.setHash(key,data);
            curBot.setBBHash(key, data);
        }
        public string getBBHash(string key)
        {
            try
            { 
                //BBDict[key] =curBot.myChemistry.m_cBus.getHash(key)
                return curBot.getBBHash(key);
            }
            catch
            {
                return "";
            }
        }

        public  void sayResponse(string message)
        {
            if (!message.ToUpper().Contains("IGNORENOP"))
            {
                //myChemistry.m_cBus.setHash("mdollsay", myResp);
                //myChemistry.m_cBus.setHash("mdollsayuttid", lastuutid.ToString());
                setBBHash("TTSText", message);
                Random Rgen = new Random();
           
                int myUUID = Rgen.Next(Int32.MaxValue);
                //curBot.myChemistry.m_cBus.setHash("TTSuuid", lastuutid.ToString());
                setBBHash("TTSuuid", myUUID.ToString());
                Console.WriteLine("sayResponse :{0}:{1}", myUUID.ToString(), message);

                String lsaThat = message.Replace(" ", " TTX");
                lsaThat = lsaThat.Replace(".", " ");
                lsaThat = lsaThat.Replace("?", " ");
                lsaThat = lsaThat.Replace("!", " ");
                lsaThat = lsaThat.Replace(",", " ");
                lsaThat += " " + message;
                setBBHash("lsathat", lsaThat);

            }

        }
    }

    
}