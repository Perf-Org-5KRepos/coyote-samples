﻿// ------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------------------------------------------

using Microsoft.Coyote;
using Microsoft.Coyote.Actors;

namespace Coyote.Examples.BoundedAsync
{
    /// <summary>
    /// Process machine that communicates with its left and right
    /// neighbour machines.
    /// </summary>
    internal class Process : StateMachine
    {
        internal class Configure : Event
        {
            public ActorId Scheduler;

            public Configure(ActorId scheduler)
            {
                this.Scheduler = scheduler;
            }
        }

        internal class Initialize : Event
        {
            public ActorId Left;
            public ActorId Right;

            public Initialize(ActorId left, ActorId right)
            {
                this.Left = left;
                this.Right = right;
            }
        }

        internal class MyCount : Event
        {
            public int Count;

            public MyCount(int count)
            {
                this.Count = count;
            }
        }

        internal class Resp : Event { }

        internal class Req : Event { }

        /// <summary>
        /// Reference to the scheduler machine.
        /// </summary>
        private ActorId Scheduler;

        /// <summary>
        /// Reference to the left process machine.
        /// </summary>
        private ActorId Left;

        /// <summary>
        /// Reference to the right process machine.
        /// </summary>
        private ActorId Right;

        private int Count;

        /// <summary>
        /// It starts in the 'Init' state, where it receives a reference
        /// to its neighbour machines. When it receives the references,
        /// it fires a 'Req' event to the scheduler.
        /// </summary>
        [Start]
        [OnEntry(nameof(InitOnEntry))]
        [OnEventDoAction(typeof(Initialize), nameof(InitializeAction))]
        private class Init : State { }

        private void InitOnEntry()
        {
            // Receives a reference to the scheduler machine (as a payload of
            // the 'Config' event).
            this.Scheduler = (this.ReceivedEvent as Configure).Scheduler;
            this.Count = 0;
        }

        private void InitializeAction()
        {
            // Receives a reference to the left process machine (as a payload of
            // the 'Initialize' event).
            this.Left = (this.ReceivedEvent as Initialize).Left;
            // Receives a reference to the right process machine (as a payload of
            // the 'Initialize' event).
            this.Right = (this.ReceivedEvent as Initialize).Right;

            // Send a 'Req' event to the scheduler machine.
            this.SendEvent(this.Scheduler, new Req());

            // Transition to the 'Syncing' state in the end of this action.
            this.Goto<Syncing>();
        }

        /// <summary>
        /// In this state, the machine sends the current count value to its
        /// neightbour machines, and a 'Req' event to the scheduler.
        ///
        /// When the scheduler responds with a 'Resp' event, it handles it
        /// with the 'Sync' action.
        ///
        /// When the machine dequeues a 'MyCount' event, it handles it with
        /// the 'ConfirmInSync' action, which asserts that the count value
        /// is the expected one.
        /// </summary>
        [OnEventDoAction(typeof(Resp), nameof(Sync))]
        [OnEventDoAction(typeof(MyCount), nameof(ConfirmInSync))]
        private class Syncing : State { }

        private void Sync()
        {
            this.Count++;

            this.SendEvent(this.Left, new MyCount(this.Count));
            this.SendEvent(this.Right, new MyCount(this.Count));
            this.SendEvent(this.Scheduler, new Req());

            // When the count reaches the value 10, the machine halts.
            if (this.Count == 10)
            {
                this.RaiseEvent(new HaltEvent());
            }
        }

        private void ConfirmInSync()
        {
            int count = (this.ReceivedEvent as MyCount).Count;

            // Asserts that the count value is the expected one.
            this.Assert(this.Count == count || this.Count == count - 1,
                $"Received count of '{count}', while current count is {this.Count}.");
        }
    }
}
