using System.Text;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRageMath;
using Sandbox.Game.GameSystems;
using System;
using VRage.Game;
using VRage.Library;
using Sandbox.Game.Entities.Cube;
using VRage.GameServices;
using VRage.ObjectBuilders;
using System.Linq;
using Sandbox.Game.Gui;
using Sandbox.ModAPI.Interfaces.Terminal;
using ITerminalAction = Sandbox.ModAPI.Interfaces.ITerminalAction;
using Sandbox.ModAPI.Weapons;
using VRage.Game.GUI.TextPanel;
using System.Diagnostics.Eventing.Reader;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Ingame;
using IMyInventory = VRage.Game.ModAPI.Ingame.IMyInventory;
using VRage;
using IMyInventoryItem = VRage.Game.ModAPI.Ingame.IMyInventoryItem;
using System.Runtime.InteropServices.WindowsRuntime;



namespace SpaceEngineersScripting
{
    class SpaceEVBuilder : MyGridProgram
    {
/*Remove above when paste to In-Game editor. For IDE intellisense*/
        #region CodeEditor
        bool UseConveyerCheck = true;

        /* Block/Group Name*/
        static string EMGTBName = "EV.TimerBlock_EMG";         //Timer Block activate on EMG Stop
        static string LogLCDName = "EV.LogLCD";                //LCD panel for logging
        static string Pistons_Elevator_GName = "EV.Pistons_Elv";//Pistons group for elevation move
        static string Pistons_Adjust_GName = "EV.Pistons_ElvAdj";//Pistons group for adjust height
        static string Piston_UpperConn_Name = "EV.Piston_UpperConn";//Piston for moving Upper connector head
        static string Piston_LowerConn_Name = "EV.Piston_LowerConn";//Piston for moving lower connector head
        static string Connector_Upper_Name = "EV.Connector_Upper";
        static string Connector_Lower_Name = "EV.Connector_Lower";
        static string MergeBlock_Upper_Name = "EV.Merge Block_Upper";
        static string MergeBlock_Lower_Name = "EV.Merge Block_Lower";//The merge block of lower stage to make the projector and EV rail same grid
        static string EMGLocks_GName = "EV.EMGLocks";            //The landing gear used when activating EMG Stop to prevent fall off
        static string Projector_Name = "EV.Projector";             //The Projector for projecting EV rail
        static string Welders_GNmae = "EV.UpperWelders";           //Welders for building EV rail
        static string Grinders_GName = "EV.ConnGrinders";          //Grinders for removing used connector and Merge Block
        static string Containers_GName = "EV.Containers";          //Cargo Containers group for store EV parts
        static string Cargo_From_Name = "A1_Large Cargo Container 1";//The Cargo Container used when checking conveyer routes by
        static string Cargo_To_Name = "EV.CargoContainer";         //trying to tranfer item between from and to
        static string Light_CycleStop_Name = "EV.Light_CycleStop"; //If turn on the light, building will stop at end of the cycle 
        static string Sensor_GrindConn_Name = "EV.Sensor_GrindConn";
        
        /*Internal values*/
        bool InitError          = true;
        int currentLine         = 0;
        int maxInstructionCounts= 0;

        string logText;
        TimeSpan CycleTime;
        TimeSpan ElapsedTime;
        bool resetElapsedTime = true;

        /*Interface to functional blocks*/
        IMyTextPanel LogLCD;
        IMyPistonBase Piston_Elevator_ref;
        List<IMyPistonBase> Pistons_Elevator = new List<IMyPistonBase>();
        List<IMyPistonBase> Pistons_Adjust = new List<IMyPistonBase>();
        IMyPistonBase Piston_UpperConn;
        IMyPistonBase Piston_LowerConn;
        IMyShipConnector Connector_Upper;
        IMyShipConnector Connector_Lower;
        IMyShipMergeBlock MergeBlock_Upper;
        IMyShipMergeBlock MergeBlock_Lower;
        List<IMyLandingGear> EMGLocks = new List<IMyLandingGear>();
        IMyProjector Projector;
        List<IMyShipWelder> Welders = new List<IMyShipWelder>();
        List<IMyShipGrinder> Grinders = new List<IMyShipGrinder>();
        IMyTimerBlock EMGStop;
        List<IMyCargoContainer> Containers= new List<IMyCargoContainer>();
        IMyCargoContainer Cargo_From;
        IMyCargoContainer Cargo_To;
        IMyInteriorLight light_CycleStop;
        IMySensorBlock Sensor_GrindConn;

        /*Space EV Rail parts*/
        MyItemType typeSteelPlate       = new MyItemType("MyObjectBuilder_Component", "SteelPlate");
        MyItemType typeInteriorPlate    = new MyItemType("MyObjectBuilder_Component", "InteriorPlate");
        MyItemType typeConstruction     = new MyItemType("MyObjectBuilder_Component", "Construction");
        MyItemType typeSmallTube        = new MyItemType("MyObjectBuilder_Component", "SmallTube");
        MyItemType typeMotor            = new MyItemType("MyObjectBuilder_Component", "Motor");
        MyItemType typeComputer         = new MyItemType("MyObjectBuilder_Component", "Computer");

        /// <summary>
        /// Write text to the top line of the LCD
        /// </summary>
        /// <param name="str">Text to write to the LCD</param>
        void WriteTopLine(string str)
        {
            logText = str + Environment.NewLine + logText;
            LogLCD.WriteText(logText, false);
        }

        /// <summary>
        /// Set OnOff and velocity to Pistons Group
        /// </summary>
        /// <param name="blocks">List of Pistons</param>
        /// <param name="OnOff">true: turn on, false:turn off</param>
        /// <param name="velocity">In OnOff=true, set piston velocity if not zero</param>
        void OnOff_Pistons(List<IMyPistonBase> blocks,bool OnOff,float velocity=0)
        {
            for (int i = 0; i < blocks.Count; i++)
            {
                if (OnOff)
                {
                    blocks[i].Enabled = true;
                    if(velocity!=0)blocks[i].Velocity = velocity;

                }
                else blocks[i].Enabled = false;
            }
        }

        void OnOff_Welders(bool OnOff)
        {
            for(int i = 0;i < Welders.Count;i++)
            {
                Welders[i].Enabled = OnOff;
            }
        }

        void OnOff_Grinders(bool OnOff)
        {
            for (int i = 0; i < Grinders.Count; i++)
            {
                Grinders[i].Enabled = OnOff;
            }
        }

        /// <summary>
        /// Check the cargo containers has enough EV parts
        /// </summary>
        bool CheckEnoughItems()
        {
            /*Amount of Construction items*/
            MyFixedPoint steelplate = 0, interiorplate = 0, construction = 0, smalltube = 0, motor = 0, computer = 0;

            foreach (IMyCargoContainer cargo in Containers)
            {
                IMyInventory inv = cargo.GetInventory();
                steelplate += inv.GetItemAmount(typeSteelPlate);
                interiorplate += inv.GetItemAmount(typeInteriorPlate);
                construction += inv.GetItemAmount(typeConstruction);
                smalltube += inv.GetItemAmount(typeSmallTube);
                motor += inv.GetItemAmount(typeMotor);
                computer += inv.GetItemAmount(typeComputer);
            }
            if (steelplate < 3000 || interiorplate < 1000 || construction < 1000 || smalltube < 1000 || motor < 1000 || computer < 1000) return false;
            else return true;

        }
        /// <summary>
        /// Check there is working conveyor connections between From and To Cargo Container so that Steel Plate can be transferred
        /// </summary>
        /// <returns>true: Can transfered, false: the cargos has no inventory or can not transfer Steel Plate</returns>
        bool CheckConveyerConnections()
        {
            IMyInventory fromInv;
            IMyInventory toInv;

            if (!UseConveyerCheck) return true;

            fromInv = Cargo_From.GetInventory();
            toInv = Cargo_To.GetInventory();

            if (fromInv == null || toInv == null)return false;
            else
            {
                return fromInv.CanTransferItemTo(toInv, typeSteelPlate);
            }
        }

        void TurnOffDevices()
        {
            Piston_LowerConn.Enabled = false;
            Piston_UpperConn.Enabled = false;
            Projector.Enabled = false;

            OnOff_Pistons(Pistons_Elevator, false);
            OnOff_Pistons(Pistons_Adjust,false);
            OnOff_Welders(false);
            OnOff_Grinders(false);
        }

        /// <summary>
        /// Stop Self-Updating and call EMGStop as Trigger Now
        /// </summary>
        /// /// <param name="reason">Reason for stop</param>
        void Error_Stop(string reason="")
        {
            Runtime.UpdateFrequency = UpdateFrequency.None;
            EMGStop.ApplyAction("TriggerNow");
            WriteTopLine("STOP by error."+Environment.NewLine+reason);
        }

        /// <summary>
        /// Check all mechanical blokcs are in its initial state or not
        /// </summary>
        /// <returns>In its initial state: true</returns>
        bool IsBlocksInInitialState()
        {
            List<bool> states = new List<bool>();

            if (Connector_Upper.Status == MyShipConnectorStatus.Connected) states.Add(true);
            else states.Add(false);
            WriteTopLine($"{Connector_Upper.CustomName}.Locked: {states[0]}");

            if (Piston_UpperConn.Status == PistonStatus.Extended) states.Add(true );
            else states.Add(false);
            WriteTopLine($"{Piston_UpperConn.CustomName}.Extended: {states[1]}");

            if (MergeBlock_Lower.IsConnected)states.Add(true);
            else states.Add(false);
            WriteTopLine($"{MergeBlock_Lower.CustomName}.Locked: {states[2]}");

            if(Connector_Lower.Status==MyShipConnectorStatus.Connected)states.Add(true);
            else states.Add(false);
            WriteTopLine($"{Connector_Lower.CustomName}.Locked: {states[3]}");

            if(Piston_LowerConn.Status==PistonStatus.Extended)states.Add(true);
            else states.Add(false);
            WriteTopLine($"{Piston_LowerConn.CustomName}.Extended: {states[4]}");

            if (Pistons_Elevator.Count == Pistons_Elevator.Count(a => a.Status==PistonStatus.Retracted))states.Add(true);
            else states.Add(false);
            WriteTopLine($"Pistons_Elevator.Retracted: {states[5]}");

            if(EMGLocks.Count == EMGLocks.Count(c => c.IsLocked == false)) states.Add(true);
            else states.Add(false);
            WriteTopLine($"EMGLocks.Unlocked: {states[6]}");

            if (states.Count(b => b) == states.Count) return true;
            else return false;
        }
        /// <summary>
        /// Get instance of functional blokcs for space elevator
        /// </summary>
        /// <returns>Initialize success or not</returns>
        bool Init()
        {
            InitError = false;

            EMGStop = GridTerminalSystem.GetBlockWithName(EMGTBName) as IMyTimerBlock;
            if (EMGStop == null) { Echo(EMGTBName); InitError = true; }

            LogLCD = GridTerminalSystem.GetBlockWithName(LogLCDName) as IMyTextPanel;
            if (LogLCD == null) { Echo(LogLCDName); InitError = true; }
            logText = LogLCD.GetText();

            IMyBlockGroup ElevatorGroup = GridTerminalSystem.GetBlockGroupWithName(Pistons_Elevator_GName);
            if (ElevatorGroup == null) { Echo(Pistons_Elevator_GName); InitError = true; }
            else
            {
                ElevatorGroup.GetBlocksOfType<IMyPistonBase>(Pistons_Elevator);
                if (Pistons_Elevator.Count == 0) { Echo(Pistons_Elevator_GName); InitError = true; }
                else
                {
                    float m=0;
                    foreach(IMyPistonBase p in Pistons_Elevator)
                    {
                        if (p.MaxLimit > m) 
                        {
                            Piston_Elevator_ref = p;
                            m = p.MaxLimit;
                        }
                    }
                }
            }

            IMyBlockGroup AdjGroup = GridTerminalSystem.GetBlockGroupWithName(Pistons_Adjust_GName);
            if (AdjGroup == null) { Echo(Pistons_Adjust_GName); InitError = true; }
            else
            {
                AdjGroup.GetBlocksOfType<IMyPistonBase>(Pistons_Adjust);
                if (Pistons_Adjust.Count == 0) { Echo(Pistons_Adjust_GName); InitError = true; }
            }

            Piston_UpperConn = GridTerminalSystem.GetBlockWithName(Piston_UpperConn_Name) as IMyPistonBase;
            if (Piston_UpperConn == null) { Echo(Piston_UpperConn_Name); InitError = true; }

            Piston_LowerConn = GridTerminalSystem.GetBlockWithName(Piston_LowerConn_Name) as IMyPistonBase;
            if (Piston_LowerConn == null) { Echo(Piston_LowerConn_Name); InitError = true; }

            Connector_Upper = GridTerminalSystem.GetBlockWithName(Connector_Upper_Name) as IMyShipConnector;
            if (Connector_Upper == null) { Echo(Connector_Upper_Name); InitError = true; }

            Connector_Lower = GridTerminalSystem.GetBlockWithName(Connector_Lower_Name) as IMyShipConnector;
            if (Connector_Lower == null) { Echo(Connector_Lower_Name); InitError = true; }

            MergeBlock_Upper = GridTerminalSystem.GetBlockWithName(MergeBlock_Upper_Name) as IMyShipMergeBlock;
            if (MergeBlock_Upper == null) { Echo(MergeBlock_Upper_Name); InitError = true; }

            MergeBlock_Lower = GridTerminalSystem.GetBlockWithName(MergeBlock_Lower_Name) as IMyShipMergeBlock;
            if (MergeBlock_Lower == null) { Echo(MergeBlock_Lower_Name); InitError = true; }

            Projector = GridTerminalSystem.GetBlockWithName(Projector_Name) as IMyProjector;
            if (Projector == null) { Echo(Projector_Name); InitError = true; }

            IMyBlockGroup GearsGroup = GridTerminalSystem.GetBlockGroupWithName(EMGLocks_GName);
            if (GearsGroup == null) { Echo(EMGLocks_GName);InitError = true; }
            else
            {
                GearsGroup.GetBlocksOfType<IMyLandingGear>(EMGLocks);
                if (EMGLocks.Count == 0) { Echo(EMGLocks_GName); InitError = true; }
            }


            IMyBlockGroup WeldersGroup = GridTerminalSystem.GetBlockGroupWithName(Welders_GNmae);
            if (WeldersGroup == null) { Echo(Welders_GNmae); InitError = true; }
            else
            {
                WeldersGroup.GetBlocksOfType<IMyShipWelder>(Welders);
                if (Welders.Count == 0) { Echo(Welders_GNmae); InitError = true; }
            }

            IMyBlockGroup GrindersGroup = GridTerminalSystem.GetBlockGroupWithName(Grinders_GName);
            if (GrindersGroup == null) { Echo(Grinders_GName); InitError = true; }
            else
            {
                GrindersGroup.GetBlocksOfType<IMyShipGrinder>(Grinders);
                if (Grinders.Count == 0) { Echo(Grinders_GName); InitError = true; }
            }

            IMyBlockGroup ContainersGroup = GridTerminalSystem.GetBlockGroupWithName(Containers_GName);
            if (ContainersGroup == null) { Echo(Containers_GName);InitError = true; }
            else
            {
                ContainersGroup.GetBlocksOfType<IMyCargoContainer>(Containers);
                if (Containers.Count == 0) { Echo(Containers_GName); InitError = true; }
            }

            Cargo_From = GridTerminalSystem.GetBlockWithName(Cargo_From_Name)as IMyCargoContainer;
            if (Cargo_From == null) { Echo(Cargo_From_Name); InitError = true; }

            Cargo_To = GridTerminalSystem.GetBlockWithName(Cargo_To_Name) as IMyCargoContainer;
            if (Cargo_To == null) { Echo(Cargo_To_Name); InitError = true; }

            light_CycleStop = GridTerminalSystem.GetBlockWithName(Light_CycleStop_Name) as IMyInteriorLight;
            if (light_CycleStop == null) { Echo(Light_CycleStop_Name); InitError = true; }

            Sensor_GrindConn = GridTerminalSystem.GetBlockWithName(Sensor_GrindConn_Name) as IMySensorBlock;
            if (Sensor_GrindConn == null) { Echo(Sensor_GrindConn_Name);InitError = true;
            }
            if (InitError)
            {
                Echo("Initialize Failed:Not found the blocks above.");
                Runtime.UpdateFrequency = UpdateFrequency.None;
                Storage = "";
                return false;
            }
            else
            {
                Echo("Initialize Successfull.");
                return true;
            }
           
        }
        public Program()
        {

            // The constructor, called only once every session and
            // always before any other method is called. Use it to
            // initialize your script.
            if(!Init())return;

            resetElapsedTime = true;
            ElapsedTime = TimeSpan.Zero;

            if (Storage == "") { currentLine = 0; }
            else
            {
                string[] subs = Storage.Split(',');
                currentLine = int.Parse(subs[0]);
                if (subs.Length == 2)
                {
                    ElapsedTime = TimeSpan.Parse(subs[1]);
                    resetElapsedTime = false;
                }
                else
                {
                    resetElapsedTime=true;
                }
                if(currentLine>0) Runtime.UpdateFrequency = UpdateFrequency.Update1;
            }

        }

        public void Save()
        {
            // Called when the program needs to save its state. Use
            // this method to save your state to the Storage field
            // or some other means.

            Storage = currentLine.ToString();
            if (!resetElapsedTime)
            {
                Storage = Storage + "," + Convert.ToString(ElapsedTime);
            }
        }

        public void Main(string arg)
        {
            if (string.Equals(arg, "START", StringComparison.OrdinalIgnoreCase) && !InitError)
            {
                if (currentLine == 0)
                {
                    if (InitError)
                    {
                        WriteTopLine("Cycle start failed: Run without initialized.");
                        Runtime.UpdateFrequency = UpdateFrequency.None;
                        return;
                    }

                    if (!IsBlocksInInitialState())
                    {
                        WriteTopLine("Cycle start failed: The blocks are not in its initial state.");
                        Runtime.UpdateFrequency = UpdateFrequency.None;
                        return;
                    }
                    currentLine = 1;
                    resetElapsedTime = true;
                    ElapsedTime = TimeSpan.Zero;
                }
                LogLCD.ContentType = ContentType.TEXT_AND_IMAGE;
                Runtime.UpdateFrequency = UpdateFrequency.Update1;

            }
            else if (string.Equals(arg, "PAUSE", StringComparison.OrdinalIgnoreCase))
            {
                Storage = $"{currentLine},{Convert.ToString(ElapsedTime)}";
                Runtime.UpdateFrequency = UpdateFrequency.None;
                return;

            }
            else if (string.Equals(arg, "STOP", StringComparison.OrdinalIgnoreCase))
            {
                TurnOffDevices();
                Runtime.UpdateFrequency = UpdateFrequency.None;
                WriteTopLine($"STOP at Line:{currentLine}, ElapsedTime:{ElapsedTime.TotalMilliseconds/1000:N2}s");
                Storage = "";
                currentLine = 0;
                ElapsedTime= TimeSpan.Zero;
                resetElapsedTime= true;
                return;

            }
            else if (string.Equals(arg, "RESET", StringComparison.OrdinalIgnoreCase))
            {
                Init();
                Storage = "";
                currentLine = 0;
                ElapsedTime = TimeSpan.Zero;
                resetElapsedTime = true;
                maxInstructionCounts = 0;
                return;
            }
            else if (string.Equals(arg, "CLEAR", StringComparison.OrdinalIgnoreCase))
            {
                logText = "";
                LogLCD.WriteText("", false);
                LogLCD.ContentType = ContentType.TEXT_AND_IMAGE;
            }
            else if (string.Equals(arg, "STORAGE", StringComparison.OrdinalIgnoreCase))
            { WriteTopLine($"Storage currently contains:\"{Storage}\""); return; }

            else if(currentLine==0)
            {
                Runtime.UpdateFrequency = UpdateFrequency.None;
            }
            else
            {
                ElapsedTime += Runtime.TimeSinceLastRun;
                CycleTime += Runtime.TimeSinceLastRun;
                resetElapsedTime = false;
            }

            Echo($"L: {currentLine}");
            Echo($"C: {CycleTime.TotalMilliseconds/1000:N1}s");
            Echo($"E: {ElapsedTime.TotalMilliseconds/1000:N1}s");
            //Echo($"S: {Runtime.TimeSinceLastRun.TotalMilliseconds:N1}ms");

            switch (currentLine)
            {
                case 1:
                    CycleTime = TimeSpan.Zero;
                    if (!CheckEnoughItems())
                    {
                        Error_Stop("Not enough EV parts.");
                    }
                    else
                    {
                        OnOff_Welders(true);
                        Projector.Enabled = true;
                        OnOff_Grinders(false);
                        Sensor_GrindConn.Enabled = false;

                        if (MergeBlock_Upper.IsConnected) currentLine = 2;
                        else currentLine = 10;

                        resetElapsedTime = true;
                        ElapsedTime = TimeSpan.Zero;
                    }
                    break;
                case 2:///Unlock Merge Block_Upper if connected
                    if (ElapsedTime >= TimeSpan.FromSeconds(5))//timeout: 5s
                    {
                        Error_Stop("Timeout: Unable to unlock Merge Block_Upper.");
                    }
                    else if (MergeBlock_Upper.IsConnected)
                    {
                        MergeBlock_Upper.Enabled = false;
                    }
                    else
                    {
                        //WriteTopLine($"Unlock Merge Block_Upper. Elapsed time: {ElapsedTime.TotalMilliseconds/1000:N2}s");
                        currentLine = 3;
                        resetElapsedTime = true;
                        ElapsedTime = TimeSpan.Zero;
                    }
                    break;
                case 3://delay 1s
                    if (ElapsedTime >= TimeSpan.FromSeconds(1))
                    {
                        currentLine = 10;
                        resetElapsedTime = true;
                        ElapsedTime = TimeSpan.Zero;
                    }
                    break;
                case 10://Unlock Connector_Upper, turn on projector and welders.
                    if (ElapsedTime >= TimeSpan.FromSeconds(5))//timeout: 5s
                    {
                        Error_Stop("Timeout: Unable to disconnect Connector_Upper");
                    }
                    else if (Connector_Upper.Status == MyShipConnectorStatus.Connected)
                    {
                        Connector_Upper.Disconnect();
                    }
                    else
                    {
                        //WriteTopLine($"Unlock Conncecotr_Upper. Elapsed time: {ElapsedTime.TotalMilliseconds / 1000:N2}s");
                        currentLine = 20;
                        ElapsedTime = TimeSpan.Zero;
                        resetElapsedTime = true;
                    }
                    break;

                case 20://Retract and wait until Piston_UpperConn
                    if (ElapsedTime >= TimeSpan.FromSeconds(5))//Timeout
                    {
                        Error_Stop($"Timeout: Unable to retract Piston_UpperConn"+Environment.NewLine+
                            $"Piston_UpperConn positon: {Piston_UpperConn.CurrentPosition} m");
                    }
                    else if (Piston_UpperConn.Status == PistonStatus.Extended)
                    {
                        Piston_UpperConn.Enabled = true;
                        Piston_UpperConn.Velocity = -5.0f;
                    }
                    else if (Piston_UpperConn.Status == PistonStatus.Retracted)
                    {
                        WriteTopLine($"Retracted Piston_UpperConn. Elapsed time: {ElapsedTime.TotalMilliseconds / 1000:N2}s");
                        currentLine = 30;
                        ElapsedTime = TimeSpan.Zero;
                        resetElapsedTime = true;
                    }
                    break;
                case 30:
                    if (ElapsedTime >= TimeSpan.FromSeconds(60))//Timeout:60s
                    {
                        string errTxt= "Timeout: Unable to weld blocks fully"+ Environment.NewLine;

                        errTxt += $"Elevator Piston positon: {Piston_Elevator_ref.CurrentPosition} m"+ Environment.NewLine;
                        errTxt += $"Remaining blokcs: {Projector.RemainingBlocks} of {Projector.TotalBlocks}";
                        Error_Stop(errTxt);
                    }
                    // Turn on projector, weldres and Extend Elevator Pistons;Start Welding
                    else if (Piston_Elevator_ref.Status ==PistonStatus.Retracted)
                    {
                        WriteTopLine("Sart welding");
                        Projector.Enabled = true;
                        OnOff_Pistons(Pistons_Elevator, true, 0.3f);
                        OnOff_Welders(true);
                    }
                    // Wait Elevator Pistons are fully Extended and Remaining blocks of projector is 0
                    else if (Piston_Elevator_ref.Status == PistonStatus.Extended && Projector.RemainingBlocks == 0)
                    {
                        if (Pistons_Elevator.Count == Pistons_Elevator.Count(x=>x.Status==PistonStatus.Extended))
                        {
                            WriteTopLine($"Welding complete. Elapsed time: {ElapsedTime.TotalMilliseconds / 1000:N2}s");
                            currentLine = 40;
                            ElapsedTime = TimeSpan.Zero;
                            resetElapsedTime = true;
                        }
                    }
                    break;
                case 40://Wait 3s, then turn off projector and welders
                    if (ElapsedTime >= TimeSpan.FromSeconds(3))//delay: 3s
                    {
                        Projector.Enabled = false;
                        OnOff_Welders(false);
                        currentLine = 50;
                        ElapsedTime = TimeSpan.Zero;
                        resetElapsedTime = true;
                    }
                        break;
                case 50://Exntend and wait until Piston_UpperConn
                    if (ElapsedTime >= TimeSpan.FromSeconds(10))//Timeout
                    {
                        Error_Stop("Timeout failed: Unable to extend Piston_UpperConn.");
                    }
                    else if (Piston_UpperConn.Status == PistonStatus.Extended)
                    {
                        WriteTopLine($"Extended Pistons_Upper. Elapsed time: {ElapsedTime.TotalMilliseconds / 1000:N2}s");
                        currentLine = 60;
                        ElapsedTime = TimeSpan.Zero;
                        resetElapsedTime = true;
                    }
                    else if(Piston_UpperConn.CurrentPosition>=4.0f)
                    {
                        Piston_UpperConn.Velocity = 1.0f;
                    }
                    else if (Piston_UpperConn.Status == PistonStatus.Retracted)
                    {
                        Piston_UpperConn.Enabled = true;
                        Piston_UpperConn.Velocity = 4.0f;
                    }
                    break;
                case 60://Wait 1s
                    if (ElapsedTime >= TimeSpan.FromSeconds(1))//delay: 1s
                    {
                        currentLine = 70;
                        ElapsedTime = TimeSpan.Zero;
                        resetElapsedTime = true;
                    }
                    break;
                case 70://Try to lock Conncector_Upper and wait until locked
                    if (ElapsedTime >= TimeSpan.FromSeconds(5))//Timeout:5s
                    {
                        Error_Stop("Timeout: Unable to lock Connector_Upper.");
                    }
                    else if (Connector_Upper.Status!=MyShipConnectorStatus.Connected)
                    {
                        Connector_Upper.Connect();
                    }
                    else
                    {
                        WriteTopLine($"Locked Connector_Upper. Elapsed time: {ElapsedTime.TotalMilliseconds / 1000:N2}s");
                        currentLine = 75;
                        ElapsedTime = TimeSpan.Zero;
                        resetElapsedTime = true;
                    }
                    break;
                case 75://Wait 1s
                    if (ElapsedTime >= TimeSpan.FromSeconds(1))//Delay: 1s
                    {
                        currentLine = 80;
                        ElapsedTime = TimeSpan.Zero;
                        resetElapsedTime = true;
                    }
                    break;
                case 80://Unlock and wait until Merge Block_Lower is unlocked
                    if (ElapsedTime >= TimeSpan.FromSeconds(5))//Timeout:5s
                    {
                        Error_Stop("Timeout: Unable to unlock Merge Block_Lower.");
                    }
                    else if(MergeBlock_Lower.IsConnected)
                    {
                        MergeBlock_Lower.Enabled = false;
                    }
                    else
                    {
                        //WriteTopLine($"Unlocked MergeBlock_Lower. Elapsed time: {ElapsedTime.TotalMilliseconds / 1000:N2}s");
                        currentLine = 85;
                        ElapsedTime = TimeSpan.Zero;
                        resetElapsedTime = true;
                    }
                    break;
                case 85://Wait 1s, then Unlock Connector_Lower
                    if(ElapsedTime >= TimeSpan.FromSeconds(1))
                    {
                        currentLine = 90;
                        ElapsedTime = TimeSpan.Zero;
                        resetElapsedTime = true;
                    }
                    break;
                case 90://Try to unlock Connector_Lower and wait until unlocked
                    if (ElapsedTime >= TimeSpan.FromSeconds(5))//Timeout:5s
                    {
                        Error_Stop("Timeout: Unable to unlock Connector_Lower.");
                    }
                    else if (Connector_Lower.Status==MyShipConnectorStatus.Connected)
                    {
                        Connector_Lower.Disconnect();
                    }
                    else
                    {
                        //WriteTopLine($"Unlocked Connector_Lower. Elapsed time: {ElapsedTime.TotalMilliseconds / 1000:N2}s");
                        currentLine = 100;
                        ElapsedTime = TimeSpan.Zero;
                        resetElapsedTime = true;
                    }
                    break;
                case 100://Retract and wait until Piston_LowerConn
                    if (ElapsedTime >= TimeSpan.FromSeconds(5))//Timeout
                    {
                        Error_Stop("Timeout: Unable to Retract Piston_LowerConn.");
                    }
                    else if(Piston_LowerConn.Status==PistonStatus.Extended)
                    {
                        Piston_LowerConn.Enabled = true;
                        Piston_LowerConn.Velocity = -5.0f;
                    }
                    else if (Piston_LowerConn.Status == PistonStatus.Retracted)
                    {
                        WriteTopLine($"Retracted Piston_Lower. Elapsed time: {ElapsedTime.TotalMilliseconds / 1000:N2}s");
                        currentLine = 110;
                        ElapsedTime = TimeSpan.Zero;
                        resetElapsedTime = true;
                        Sensor_GrindConn.Enabled = false;
                    }
                    break;
                case 110://Turn on grinders and Retract Pistons_Elevator,then wait until retact 4m, then wait until fully retracted
                    if (ElapsedTime >= TimeSpan.FromSeconds(30))//Timeout
                    {
                        Error_Stop("Timeout: Unable to Retract Pistons_Eleavtor.");
                        break;
                    }
                    else if (Piston_Elevator_ref.Status == PistonStatus.Retracted)
                    {
                        if (Pistons_Elevator.Count == Pistons_Elevator.Count(x => x.Status == PistonStatus.Retracted))
                        {
                            WriteTopLine($"Retracted Pistons_Elevator. Elapsed time: {ElapsedTime.TotalMilliseconds / 1000:N2}s");
                            currentLine = 115;
                            ElapsedTime = TimeSpan.Zero;
                            resetElapsedTime = true;
                        }
                    }
                    else if (Piston_Elevator_ref.CurrentPosition <= 0.5f) OnOff_Pistons(Pistons_Elevator, true, -0.5f);

                    else if (Sensor_GrindConn.Enabled && Sensor_GrindConn.IsActive && (Piston_Elevator_ref.MaxLimit - Piston_Elevator_ref.CurrentPosition) >= 2.5f)
                    {
                        Error_Stop("Error detected: Unable to grind used conncetors.");
                    }
                    else if (Sensor_GrindConn.Enabled && Piston_Elevator_ref.CurrentPosition <= 5.0f) Sensor_GrindConn.Enabled = false;

                    else if (Grinders[0].Enabled && (Piston_Elevator_ref.MaxLimit - Piston_Elevator_ref.CurrentPosition) >= 1.5f)
                    {
                        Sensor_GrindConn.Enabled = true;
                        OnOff_Grinders(false);
                        OnOff_Pistons(Pistons_Elevator, true, -4.0f);
                    }
                    else if (Piston_Elevator_ref.Status==PistonStatus.Extended)
                    {
                        Sensor_GrindConn.Enabled = false;
                        WriteTopLine("Retract Pistons_Elevator and grind used connection parts");
                        OnOff_Grinders(true);
                        OnOff_Pistons(Pistons_Elevator, true, -0.5f);
                    }
                    break;
                case 115:
                    if (ElapsedTime >= TimeSpan.FromSeconds(1))//delay 1s
                    {
                        currentLine = 120;
                        ElapsedTime = TimeSpan.Zero;
                        resetElapsedTime = true;
                    }
                    break;
                case 120://Extend and wait until Piston_LowerConn is extended
                    if (ElapsedTime >= TimeSpan.FromSeconds(10))//Timeout
                    {
                        Error_Stop("Timeout: Unable to Exnted Piston_LowerConn.");
                    }
                    else if (Piston_LowerConn.Status == PistonStatus.Extended)
                    {
                        WriteTopLine($"Extended Piston_LowerConn. Elapsed time: {ElapsedTime.TotalMilliseconds / 1000:N2}s");
                        currentLine = 125;
                        ElapsedTime = TimeSpan.Zero;
                        resetElapsedTime = true;
                    }
                    else if (Piston_LowerConn.CurrentPosition >= 4.0f) Piston_LowerConn.Velocity = 1.0f;
                    else if (Piston_LowerConn.Status == PistonStatus.Retracted)
                    {
                        Piston_LowerConn.Enabled = true;
                        Piston_LowerConn.Velocity = 4.0f;
                    }
                    break;
                case 125://Wait 1s
                    if(ElapsedTime >= TimeSpan.FromSeconds(1))
                    {
                        currentLine = 130;
                        ElapsedTime = TimeSpan.Zero;
                        resetElapsedTime = true;
                    }
                    break;
                case 130://Try to lock and wait until Merge Block_Lower is locked
                    if (ElapsedTime >= TimeSpan.FromSeconds(5))//Timeout:5s
                    {
                        Error_Stop("Timeout: Unable to merge to Merge Block_Lower.");
                    }
                    else if(MergeBlock_Lower.State!=MergeState.Locked)
                    {
                        MergeBlock_Lower.Enabled = true;
                    }
                    else
                    {
                        WriteTopLine($"Locked MergeBlock_Lower. Elapsed time: {ElapsedTime.TotalMilliseconds / 1000:N2}s");
                        currentLine = 140;
                        ElapsedTime = TimeSpan.Zero;
                        resetElapsedTime = true;
                    }
                    break;
                case 140://Try to lock Connector_Lower and wait until Connector_Lower is locked
                    if(ElapsedTime >=TimeSpan.FromSeconds(5))
                    {
                        Error_Stop("Timeout: Unable to lock Connector_Lower.");
                    }
                    else if (Connector_Lower.Status!=MyShipConnectorStatus.Connected)
                    {
                        if (CheckConveyerConnections())
                        {
                            WriteTopLine("Conveyer connections check success.");
                            Connector_Lower.Connect();
                        }
                        else
                        {
                            Error_Stop("Conveyer connections failure: Unable to access between EV builder and base station.");
                        } 
                    }
                    else
                    {
                        WriteTopLine($"Locked Connector_Lower. Elapsed time: {ElapsedTime.TotalMilliseconds / 1000:N2}s");
                        currentLine = 150;
                        ElapsedTime = TimeSpan.Zero;
                        resetElapsedTime = true;
                    }
                    break;
                case 150:
                    if (light_CycleStop.Enabled)//If light_CycleStop is On, stop at the end of the cycle
                    {
                        light_CycleStop.Enabled = false;
                        Runtime.UpdateFrequency = UpdateFrequency.None;
                        WriteTopLine($"Stoped Cycle by order. Cycle Time: {CycleTime.TotalMilliseconds/1000:N2}s");
                        currentLine = 0;
                        ElapsedTime = TimeSpan.Zero;
                        CycleTime = TimeSpan.Zero;
                        resetElapsedTime = true;
                    }
                    else if (ElapsedTime >= TimeSpan.FromSeconds(1))//Wait 1s then return to case 1
                    {
                        WriteTopLine($"Build cycle complete. Cycle Time: {CycleTime.TotalMilliseconds/1000:N2}s");
                        currentLine = 1;
                        ElapsedTime = TimeSpan.Zero;
                        CycleTime   = TimeSpan.Zero;
                        resetElapsedTime = true;
                    }
                    break;
            }
            if(Runtime.CurrentInstructionCount>maxInstructionCounts)maxInstructionCounts = Runtime.CurrentInstructionCount;
            Echo("CurrentInstructionCount: " + Runtime.CurrentInstructionCount);
            Echo("MaxInstructionCount: " + maxInstructionCounts);
            //if (resetElapsedTime) Storage = currentLine.ToString();
            //else Storage = currentLine.ToString() + "," + ElapsedTime.ToString();
            //Echo(Storage);
            return;
        }

        /*void test()
        {
            IMyInventory fromInv;
            IMyInventory toInv;
            IMyCargoContainer Cargo_From;
            IMyCargoContainer Cargo_To;

            Cargo_From = GridTerminalSystem.GetBlockWithName("A1_Large Cargo Container 1") as IMyCargoContainer;
            if (Cargo_From == null) { Echo("A1_Large Cargo Container 1");}

            Cargo_To = GridTerminalSystem.GetBlockWithName("EV.Small Cargo Container") as IMyCargoContainer;
            if (Cargo_To == null) { Echo("EV.Small Cargo Container");  }

            fromInv = Cargo_From.GetInventory();
            toInv = Cargo_To.GetInventory();

            if (fromInv == null || toInv == null)
            {
                Echo("The Cargo containers has no inventory.");
                return;
            }
            var obj= fromInv.GetItemAt(0);
            if (obj == null) {
                Echo("Inventory has no item");
                return;
            }
            else
            {
                MyInventoryItem item = (MyInventoryItem)obj;
                Echo(item.ToString());
                Echo(fromInv.CanTransferItemTo(fromInv, item.Type).ToString());
            }

            IMySensorBlock Sensor_GrindConn = GridTerminalSystem.GetBlockWithName("EV.Sensor_GrindConn") as IMySensorBlock;
            Echo(Sensor_GrindConn.IsActive.ToString());

        }*/

        #endregion
/*Remove below when paste to In-Game editor. For IDE intellisense*/        
    }
}
