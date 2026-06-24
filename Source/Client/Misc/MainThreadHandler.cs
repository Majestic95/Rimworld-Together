using RTShared;
using RTShared.Misc;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using static GameClient.Hooks.TCPNetwork.ClientNetwork;

namespace GameClient.Misc
{
    public class MainThreadHandler : MonoBehaviour
    {
        public static MainThreadHandler Instance { get; private set; }

        private static Queue<Action> ActionQueue { get; set; } = new Queue<Action>();

        public MainThreadHandler() { Instance = this; }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);

            Printer.Message($"Created dispatcher for version > {CommonValues.ExecutableVersion}");
        }

        private void Update() 
        { 
            ExecuteAllQueue();

            if (SessionHandler.CurrentNetworkState == ClientNetworkState.Connected)
            {
                DoPerFrameMethods();
            }
        }

        public void DoOnStartMethods()
        {
            foreach (MethodInfo method in MethodGatherer.OnStartMethods)
            {
                method.Invoke(null, null);
            }
        }

        public void DoOnEndMethods()
        {
            foreach (MethodInfo method in MethodGatherer.OnEndMethods)
            {
                method.Invoke(null, null);
            }
        }

        private void DoPerFrameMethods()
        {
            foreach (MethodInfo method in MethodGatherer.PerFrameMethods)
            {
                method.Invoke(null, null);
            }
        }

        public void DoOnSynchronousStartMethods()
        {
            foreach (MethodInfo method in MethodGatherer.OnSynchronousStartMethods)
            {
                method.Invoke(null, null);
            }
        }

        public void DoOnSynchronousEndMethods()
        {
            foreach (MethodInfo method in MethodGatherer.OnSynchronousEndMethods)
            {
                method.Invoke(null, null);
            }
        }

        public void Enqueue(Action action) { Enqueue(ActionWrapper(action)); }

        private void Enqueue(IEnumerator action)
        {
            lock (ActionQueue)
            {
                ActionQueue.Enqueue(() =>
                {
                    StartCoroutine(action);
                });
            }
        }

        private void ExecuteAllQueue()
        {
            lock (ActionQueue)
            {
                while (ActionQueue.Count > 0)
                {
                    Action queued = ActionQueue.Dequeue();
                    try { queued.Invoke(); }
                    catch (Exception e) { Printer.Error($"MainThreadHandler dispatch failed: {e}"); }
                }
            }
        }

        IEnumerator ActionWrapper(Action action)
        {
            // Per-action try/catch — a thrown packet handler must not tear down the connection.
            try { action(); }
            catch (Exception e) { Printer.Error($"MainThreadHandler queued action failed: {e}"); }
            yield return null;
        }
    }
}
