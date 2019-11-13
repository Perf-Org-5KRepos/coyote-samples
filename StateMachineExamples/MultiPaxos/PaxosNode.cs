﻿// ------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Coyote;
using Microsoft.Coyote.Actors;

namespace Coyote.Examples.MultiPaxos
{
    internal class PaxosNode : StateMachine
    {
        internal class Config : Event
        {
            public int MyRank;

            public Config(int id)
            {
                this.MyRank = id;
            }
        }

        internal class AllNodes : Event
        {
            public List<ActorId> Nodes;

            public AllNodes(List<ActorId> nodes)
            {
                this.Nodes = nodes;
            }
        }

        internal class Prepare : Event
        {
            public ActorId Node;
            public int NextSlotForProposer;
            public Tuple<int, int> NextProposal;
            public int MyRank;

            public Prepare(ActorId id, int nextSlot, Tuple<int, int> nextProposal, int myRank)
            {
                this.Node = id;
                this.NextSlotForProposer = nextSlot;
                this.NextProposal = nextProposal;
                this.MyRank = myRank;
            }
        }

        internal class Accepted : Event
        {
            public int Slot;
            public int Round;
            public int Server;
            public int Value;

            public Accepted(int slot, int round, int server, int value)
            {
                this.Slot = slot;
                this.Round = round;
                this.Server = server;
                this.Value = value;
            }
        }

        internal class Chosen : Event
        {
            public int Slot;
            public int Round;
            public int Server;
            public int Value;

            public Chosen(int slot, int round, int server, int value)
            {
                this.Slot = slot;
                this.Round = round;
                this.Server = server;
                this.Value = value;
            }
        }

        internal class Agree : Event
        {
            public int Slot;
            public int Round;
            public int Server;
            public int Value;

            public Agree(int slot, int round, int server, int value)
            {
                this.Slot = slot;
                this.Round = round;
                this.Server = server;
                this.Value = value;
            }
        }

        internal class Accept : Event
        {
            public ActorId Node;
            public int NextSlotForProposer;
            public Tuple<int, int> NextProposal;
            public int ProposeVal;

            public Accept(ActorId id, int nextSlot, Tuple<int, int> nextProposal, int proposeVal)
            {
                this.Node = id;
                this.NextSlotForProposer = nextSlot;
                this.NextProposal = nextProposal;
                this.ProposeVal = proposeVal;
            }
        }

        internal class Reject : Event
        {
            public int Round;
            public Tuple<int, int> Proposal;

            public Reject(int round, Tuple<int, int> proposal)
            {
                this.Round = round;
                this.Proposal = proposal;
            }
        }

        internal class Update : Event
        {
            public int V1;
            public int V2;

            public Update(int v1, int v2)
            {
                this.V1 = v1;
                this.V2 = v2;
            }
        }

        private Tuple<int, ActorId> CurrentLeader;
        private ActorId LeaderElectionService;

        private List<ActorId> Acceptors;
        private int CommitValue;
        private int ProposeVal;
        private int Majority;
        private int MyRank;
        private Tuple<int, int> NextProposal;
        private Tuple<int, int, int> ReceivedAgree;
        private int MaxRound;
        private int AcceptCount;
        private int AgreeCount;
        private ActorId Timer;
        private int NextSlotForProposer;

        private Dictionary<int, Tuple<int, int, int>> AcceptorSlots;

        private Dictionary<int, Tuple<int, int, int>> LearnerSlots;
        private int LastExecutedSlot;

        [Start]
        [OnEntry(nameof(InitOnEntry))]
        [OnEventGotoState(typeof(Local), typeof(PerformOperation))]
        [OnEventDoAction(typeof(PaxosNode.Config), nameof(Configure))]
        [OnEventDoAction(typeof(PaxosNode.AllNodes), nameof(UpdateAcceptors))]
        [DeferEvents(typeof(LeaderElection.Ping))]
        private class Init : State { }

        private void InitOnEntry()
        {
            this.Acceptors = new List<ActorId>();
            this.AcceptorSlots = new Dictionary<int, Tuple<int, int, int>>();
            this.LearnerSlots = new Dictionary<int, Tuple<int, int, int>>();
        }

        private void Configure()
        {
            this.MyRank = (this.ReceivedEvent as PaxosNode.Config).MyRank;

            this.CurrentLeader = Tuple.Create(this.MyRank, this.Id);
            this.MaxRound = 0;

            this.Timer = this.CreateActor(typeof(Timer));
            this.SendEvent(this.Timer, new Timer.Config(this.Id, 10));

            this.LastExecutedSlot = -1;
            this.NextSlotForProposer = 0;
        }

        [OnEventPushState(typeof(GoPropose), typeof(ProposeValuePhase1))]
        [OnEventPushState(typeof(PaxosNode.Chosen), typeof(RunLearner))]
        [OnEventDoAction(typeof(PaxosNode.Update), nameof(CheckIfLeader))]
        [OnEventDoAction(typeof(PaxosNode.Prepare), nameof(PrepareAction))]
        [OnEventDoAction(typeof(PaxosNode.Accept), nameof(AcceptAction))]
        [OnEventDoAction(typeof(LeaderElection.Ping), nameof(ForwardToLE))]
        [OnEventDoAction(typeof(LeaderElection.NewLeader), nameof(UpdateLeader))]
        [IgnoreEvents(typeof(PaxosNode.Agree), typeof(PaxosNode.Accepted), typeof(Timer.TimeoutEvent), typeof(PaxosNode.Reject))]
        private class PerformOperation : State { }

        [OnEntry(nameof(ProposeValuePhase1OnEntry))]
        [OnEventGotoState(typeof(PaxosNode.Reject), typeof(ProposeValuePhase1), nameof(ProposeValuePhase1RejectAction))]
        [OnEventGotoState(typeof(Success), typeof(ProposeValuePhase2), nameof(ProposeValuePhase1SuccessAction))]
        [OnEventGotoState(typeof(Timer.TimeoutEvent), typeof(ProposeValuePhase1))]
        [OnEventDoAction(typeof(PaxosNode.Agree), nameof(CountAgree))]
        [IgnoreEvents(typeof(PaxosNode.Accepted))]
        private class ProposeValuePhase1 : State { }

        private void ProposeValuePhase1OnEntry()
        {
            this.AgreeCount = 0;
            this.NextProposal = this.GetNextProposal(this.MaxRound);
            this.ReceivedAgree = Tuple.Create(-1, -1, -1);

            foreach (var acceptor in this.Acceptors)
            {
                this.SendEvent(acceptor, new PaxosNode.Prepare(this.Id, this.NextSlotForProposer, this.NextProposal, this.MyRank));
            }

            this.Monitor<ValidityCheck>(new ValidityCheck.MonitorProposerSent(this.ProposeVal));
            this.SendEvent(this.Timer, new Timer.StartTimerEvent());
        }

        private void ProposeValuePhase1RejectAction()
        {
            var round = (this.ReceivedEvent as PaxosNode.Reject).Round;

            if (this.NextProposal.Item1 <= round)
            {
                this.MaxRound = round;
            }

            this.SendEvent(this.Timer, new Timer.CancelTimerEvent());
        }

        private void ProposeValuePhase1SuccessAction()
        {
            this.SendEvent(this.Timer, new Timer.CancelTimerEvent());
        }

        [OnEntry(nameof(ProposeValuePhase2OnEntry))]
        [OnExit(nameof(ProposeValuePhase2OnExit))]
        [OnEventGotoState(typeof(PaxosNode.Reject), typeof(ProposeValuePhase1), nameof(ProposeValuePhase2RejectAction))]
        [OnEventGotoState(typeof(Timer.TimeoutEvent), typeof(ProposeValuePhase1))]
        [OnEventDoAction(typeof(PaxosNode.Accepted), nameof(CountAccepted))]
        [IgnoreEvents(typeof(PaxosNode.Agree))]
        private class ProposeValuePhase2 : State { }

        private void ProposeValuePhase2OnEntry()
        {
            this.AcceptCount = 0;
            this.ProposeVal = this.GetHighestProposedValue();

            this.Monitor<BasicPaxosInvariant_P2b>(new BasicPaxosInvariant_P2b.MonitorValueProposed(
                this.Id, this.NextSlotForProposer, this.NextProposal, this.ProposeVal));
            this.Monitor<ValidityCheck>(new ValidityCheck.MonitorProposerSent(this.ProposeVal));

            foreach (var acceptor in this.Acceptors)
            {
                this.SendEvent(acceptor, new PaxosNode.Accept(this.Id, this.NextSlotForProposer, this.NextProposal, this.ProposeVal));
            }

            this.SendEvent(this.Timer, new Timer.StartTimerEvent());
        }

        private void ProposeValuePhase2OnExit()
        {
            if (this.ReceivedEvent.GetType() == typeof(PaxosNode.Chosen))
            {
                this.Monitor<BasicPaxosInvariant_P2b>(new BasicPaxosInvariant_P2b.MonitorValueChosen(
                    this.Id, this.NextSlotForProposer, this.NextProposal, this.ProposeVal));

                this.SendEvent(this.Timer, new Timer.CancelTimerEvent());

                this.Monitor<ValidityCheck>(new ValidityCheck.MonitorProposerChosen(this.ProposeVal));

                this.NextSlotForProposer++;
            }
        }

        private void ProposeValuePhase2RejectAction()
        {
            var round = (this.ReceivedEvent as PaxosNode.Reject).Round;

            if (this.NextProposal.Item1 <= round)
            {
                this.MaxRound = round;
            }

            this.SendEvent(this.Timer, new Timer.CancelTimerEvent());
        }

        [OnEntry(nameof(RunLearnerOnEntry))]
        [IgnoreEvents(typeof(PaxosNode.Agree), typeof(PaxosNode.Accepted), typeof(Timer.TimeoutEvent),
            typeof(PaxosNode.Prepare), typeof(PaxosNode.Reject), typeof(PaxosNode.Accept))]
        [DeferEvents(typeof(LeaderElection.NewLeader))]
        private class RunLearner : State { }

        private void RunLearnerOnEntry()
        {
            var slot = (this.ReceivedEvent as PaxosNode.Chosen).Slot;
            var round = (this.ReceivedEvent as PaxosNode.Chosen).Round;
            var server = (this.ReceivedEvent as PaxosNode.Chosen).Server;
            var value = (this.ReceivedEvent as PaxosNode.Chosen).Value;

            this.LearnerSlots[slot] = Tuple.Create(round, server, value);

            if (this.CommitValue == value)
            {
                this.Pop();
            }
            else
            {
                this.ProposeVal = this.CommitValue;
                this.RaiseEvent(new GoPropose());
            }
        }

        private void UpdateAcceptors()
        {
            var acceptors = (this.ReceivedEvent as PaxosNode.AllNodes).Nodes;

            this.Acceptors = acceptors;

            this.Majority = (this.Acceptors.Count / 2) + 1;
            this.Assert(this.Majority == 2, "Majority is not 2");

            this.LeaderElectionService = this.CreateActor(typeof(LeaderElection));
            this.SendEvent(this.LeaderElectionService, new LeaderElection.Config(this.Acceptors, this.Id, this.MyRank));

            this.RaiseEvent(new Local());
        }

        private void CheckIfLeader()
        {
            var e = this.ReceivedEvent as PaxosNode.Update;
            if (this.CurrentLeader.Item1 == this.MyRank)
            {
                this.CommitValue = e.V2;
                this.ProposeVal = this.CommitValue;
                this.RaiseEvent(new GoPropose());
            }
            else
            {
                this.SendEvent(this.CurrentLeader.Item2, new PaxosNode.Update(e.V1, e.V2));
            }
        }

        private void PrepareAction()
        {
            var proposer = (this.ReceivedEvent as PaxosNode.Prepare).Node;
            var slot = (this.ReceivedEvent as PaxosNode.Prepare).NextSlotForProposer;
            var round = (this.ReceivedEvent as PaxosNode.Prepare).NextProposal.Item1;
            var server = (this.ReceivedEvent as PaxosNode.Prepare).NextProposal.Item2;

            if (!this.AcceptorSlots.ContainsKey(slot))
            {
                this.SendEvent(proposer, new PaxosNode.Agree(slot, -1, -1, -1));
                return;
            }

            if (LessThan(round, server, this.AcceptorSlots[slot].Item1, this.AcceptorSlots[slot].Item2))
            {
                this.SendEvent(proposer, new PaxosNode.Reject(slot, Tuple.Create(this.AcceptorSlots[slot].Item1,
                    this.AcceptorSlots[slot].Item2)));
            }
            else
            {
                this.SendEvent(proposer, new PaxosNode.Agree(slot, this.AcceptorSlots[slot].Item1,
                    this.AcceptorSlots[slot].Item2, this.AcceptorSlots[slot].Item3));
                this.AcceptorSlots[slot] = Tuple.Create(this.AcceptorSlots[slot].Item1, this.AcceptorSlots[slot].Item2, -1);
            }
        }

        private void AcceptAction()
        {
            var e = this.ReceivedEvent as PaxosNode.Accept;

            var proposer = e.Node;
            var slot = e.NextSlotForProposer;
            var round = e.NextProposal.Item1;
            var server = e.NextProposal.Item2;
            var value = e.ProposeVal;

            if (this.AcceptorSlots.ContainsKey(slot))
            {
                if (!IsEqual(round, server, this.AcceptorSlots[slot].Item1, this.AcceptorSlots[slot].Item2))
                {
                    this.SendEvent(proposer, new PaxosNode.Reject(slot, Tuple.Create(this.AcceptorSlots[slot].Item1,
                        this.AcceptorSlots[slot].Item2)));
                }
                else
                {
                    this.AcceptorSlots[slot] = Tuple.Create(round, server, value);
                    this.SendEvent(proposer, new PaxosNode.Accepted(slot, round, server, value));
                }
            }
        }

        private void ForwardToLE()
        {
            this.SendEvent(this.LeaderElectionService, this.ReceivedEvent);
        }

        private void UpdateLeader()
        {
            var e = this.ReceivedEvent as LeaderElection.NewLeader;
            this.CurrentLeader = Tuple.Create(e.Rank, e.CurrentLeader);
        }

        private void CountAgree()
        {
            var slot = (this.ReceivedEvent as PaxosNode.Agree).Slot;
            var round = (this.ReceivedEvent as PaxosNode.Agree).Round;
            var server = (this.ReceivedEvent as PaxosNode.Agree).Server;
            var value = (this.ReceivedEvent as PaxosNode.Agree).Value;

            if (slot == this.NextSlotForProposer)
            {
                this.AgreeCount++;
                if (LessThan(this.ReceivedAgree.Item1, this.ReceivedAgree.Item2, round, server))
                {
                    this.ReceivedAgree = Tuple.Create(round, server, value);
                }

                if (this.AgreeCount == this.Majority)
                {
                    this.RaiseEvent(new Success());
                }
            }
        }

        private void CountAccepted()
        {
            var e = this.ReceivedEvent as PaxosNode.Accepted;

            var slot = e.Slot;
            var round = e.Round;
            var server = e.Server;

            if (slot == this.NextSlotForProposer)
            {
                if (IsEqual(round, server, this.NextProposal.Item1, this.NextProposal.Item2))
                {
                    this.AcceptCount++;
                }

                if (this.AcceptCount == this.Majority)
                {
                    this.RaiseEvent(new PaxosNode.Chosen(e.Slot, e.Round, e.Server, e.Value));
                }
            }
        }

        private void RunReplicatedMachine()
        {
            while (true)
            {
                if (this.LearnerSlots.ContainsKey(this.LastExecutedSlot + 1))
                {
                    this.LastExecutedSlot++;
                }
                else
                {
                    return;
                }
            }
        }

        private int GetHighestProposedValue()
        {
            if (this.ReceivedAgree.Item2 != -1)
            {
                return this.ReceivedAgree.Item2;
            }
            else
            {
                return this.CommitValue;
            }
        }

        private Tuple<int, int> GetNextProposal(int maxRound)
        {
            return Tuple.Create(maxRound + 1, this.MyRank);
        }

        private static bool IsEqual(int round1, int server1, int round2, int server2)
        {
            if (round1 == round2 && server1 == server2)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        private static bool LessThan(int round1, int server1, int round2, int server2)
        {
            if (round1 < round2)
            {
                return true;
            }
            else if (round1 == round2)
            {
                if (server1 < server2)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }
    }
}
