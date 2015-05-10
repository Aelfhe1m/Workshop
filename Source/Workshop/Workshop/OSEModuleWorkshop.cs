﻿namespace Workshop
{
    using System;
    using System.Collections.Generic;

    using KIS;

    using UnityEngine;

    public class OseModuleWorkshop : PartModule
    {
        private AvailablePart _builtPart;
        private double _resourcesUsed;
        private double _resourcesNeeded;

        private readonly Clock _clock;
        private readonly ResourceBroker _broker;
        private readonly WorkshopWindow _window;
        private readonly WorkshopQueue _queue;
        private readonly List<Demand> _upkeep;
        private readonly List<Demand> _input;

        [KSPField]
        public string Input = "";

        [KSPField]
        public string Upkeep = "";

        [KSPField]
        public int MinimumCrew = 2;

        [KSPField(guiName = "Status", guiActive = true)]
        public string Status = "Online";

        [KSPField(isPersistant = false, guiActive = true, guiActiveEditor = false, guiName = "Progress", guiUnits = "%", guiFormat = "F1")]
        [UI_ProgressBar(minValue = 0, maxValue = 100F)]
        public float Progress = 0;

        [KSPEvent(guiActive = true, guiName = "Open Workbench")]
        public void ContextMenuOnOpenWorkbench()
        {
            _window.Visible = true;
        }

        public OseModuleWorkshop()
        {
            _clock = new Clock();
            _broker = new ResourceBroker();
            _queue = new WorkshopQueue();
            _window = new WorkshopWindow(_queue);
            _upkeep = new List<Demand>();
            _input = new List<Demand>();
        }

                public override void OnLoad(ConfigNode node)
        {
            LoadModuleState(node);
            LoadUpkeep();
            LoadInput();
            base.OnLoad(node);
        }

        private void LoadModuleState(ConfigNode node)
        {
            foreach (ConfigNode cn in node.nodes)
            {
                if (cn.name == "BUILTPART" && cn.HasValue("Name") && cn.HasValue("ResourcesNeeded") && cn.HasValue("ResourcesUsed"))
                {
                    var availablePart = PartLoader.getPartInfoByName(cn.GetValue("Name"));
                    if (availablePart != null)
                    {
                        _builtPart = availablePart;
                        _resourcesNeeded = double.Parse(cn.GetValue("ResourcesNeeded"));
                        _resourcesUsed = double.Parse(cn.GetValue("ResourcesUsed"));
                    }
                }
                if (cn.name == "QUEUEDPART" && cn.HasValue("Name"))
                {
                    var availablePart = PartLoader.getPartInfoByName(cn.GetValue("Name"));
                    _queue.Add(availablePart);
                }
            }
        }

        private void LoadUpkeep()
        {
            var resources = Upkeep.Split(',');
            for (int i = 0; i < resources.Length; i+=2)
            {
                _upkeep.Add(new Demand
                {
                    ResourceName = resources[i],
                    Ratio = float.Parse(resources[i + 1])
                });
            }
        }

        private void LoadInput()
        {
            var resources = Input.Split(',');
            for (int i = 0; i < resources.Length; i+=2)
            {
                _input.Add(new Demand
                {
                    ResourceName = resources[i],
                    Ratio = float.Parse(resources[i + 1])
                });
            }
        }

        public override void OnSave(ConfigNode node)
        {
            if (_builtPart != null)
            {
                var builtPartNode = node.AddNode("BUILTPART");
                builtPartNode.AddValue("Name", _builtPart.name);
                builtPartNode.AddValue("ResourcesNeeded", _resourcesNeeded);
                builtPartNode.AddValue("ResourcesUsed", _resourcesUsed);
            }

            foreach (var queuedPart in _queue)
            {
                var queuedPartNode = node.AddNode("QUEUEDPART");
                queuedPartNode.AddValue("Name", queuedPart.name);
            }

            base.OnSave(node);
        }

        public override void OnUpdate()
        {
            var deltaTime = _clock.GetDeltaTime();
            try
            {
                if (_builtPart != null)
                {
                    if (Progress >= 100)
                    {
                        FinishManufacturing();
                    }
                    else
                    {
                        ExecuteManufacturing(deltaTime);
                    }
                }
                else
                {
                    StartManufacturing();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[OSE] - OseModuleWorkshop_OnUpdate - " + ex.Message);
            }
            base.OnUpdate();
        }

        private void StartManufacturing()
        {
            var nextQueuedPart = _queue.Pop();
            if (nextQueuedPart != null)
            {
                _resourcesNeeded = nextQueuedPart.GetResourcesNeeded(_input);
                _builtPart = nextQueuedPart;
            }
        }

        private void ExecuteManufacturing(double deltaTime)
        {
            var preRequisitesMessage = CheckPrerequisites(deltaTime);

            if (preRequisitesMessage != "Ok")
            {
                Status = preRequisitesMessage;
            }
            else
            {
                Status = "Building " + _builtPart.title;
                foreach (var res in _upkeep)
                {
                    _broker.RequestResource(part, res.ResourceName, res.Ratio * deltaTime);
                }
                foreach (var res in _input)
                {
                    _resourcesUsed += _broker.RequestResource(part, res.ResourceName, res.Ratio * deltaTime);
                }
            }
            Progress = (float)(_resourcesUsed / _resourcesNeeded * 100);
        }

        private void FinishManufacturing()
        {
            if (AddToContainer(_builtPart))
            {
                _builtPart = null;
                _resourcesUsed = 0;
                _resourcesNeeded = 0;
                Progress = 0;
                Status = "Online";
            }
            else
            {
                Status = "Not enough free space";
            }
        }

        public override void OnInactive()
        {
            _window.Visible = false;
            base.OnInactive();
        }

        private string CheckPrerequisites(double deltaTime)
        {
            if (part.protoModuleCrew.Count < MinimumCrew)
            {
                return "Not enough Crew to operate";
            }
            foreach (var res in _upkeep)
            {
                if(_broker.AmountAvailable(part, res.ResourceName) < (res.Ratio * deltaTime))
                {
                    return "Not enough " + res.ResourceName;
                }
            }
            foreach (var res in _input)
            {
                if (_broker.AmountAvailable(part, res.ResourceName) < (res.Ratio * deltaTime))
                {
                    return "Not enough " + res.ResourceName;
                }
            }
            return "Ok";
        }

        private bool AddToContainer(AvailablePart availablePart)
        {
            var kisModuleContainers = vessel.FindPartModulesImplementing<ModuleKISInventory>();

            if (kisModuleContainers == null || kisModuleContainers.Count == 0)
            {
                throw new Exception("No KIS Container found");
            }

            foreach (var container in kisModuleContainers)
            {
                if (container.GetContentVolume() + KIS_Shared.GetPartVolume(availablePart.partPrefab) < container.maxVolume)
                {
                    container.AddItem(availablePart.partPrefab);
                    return true;
                }
            }
            return false;
        }
    }
}
