﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Microsoft.MixedReality.Toolkit.Input;
using Microsoft.MixedReality.Toolkit.Utilities;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;

namespace Microsoft.MixedReality.Toolkit.UI
{
    /// <summary>
    /// Uses input and action data to declare a set of states
    /// Maintains a collection of themes that react to state changes and provide sensory feedback
    /// Passes state information and input data on to receivers that detect patterns and does stuff.
    /// </summary>
    [System.Serializable]
    [HelpURL("https://microsoft.github.io/MixedRealityToolkit-Unity/Documentation/README_Interactable.html")]
    public class Interactable :
        MonoBehaviour,
        IMixedRealityFocusChangedHandler,
        IMixedRealityFocusHandler,
        IMixedRealityInputHandler,
        IMixedRealitySpeechHandler,
        IMixedRealityTouchHandler,
        IMixedRealityInputHandler<Vector2>,
        IMixedRealityInputHandler<Vector3>,
        IMixedRealityInputHandler<MixedRealityPose>
    {
        /// <summary>
        /// Pointers that are focusing the interactable
        /// </summary>
        public List<IMixedRealityPointer> FocusingPointers => focusingPointers;
        protected readonly List<IMixedRealityPointer> focusingPointers = new List<IMixedRealityPointer>();

        /// <summary>
        /// Input sources that are pressing the interactable
        /// </summary>
        public HashSet<IMixedRealityInputSource> PressingInputSources => pressingInputSources;
        protected readonly HashSet<IMixedRealityInputSource> pressingInputSources = new HashSet<IMixedRealityInputSource>();

        [FormerlySerializedAs("States")]
        [SerializeField]
        private States states;

        /// <summary>
        /// A collection of states and basic state logic
        /// </summary>
        public States States
        {
            get { return states; }
            set
            {
                states = value;
                SetupStates();
            }
        }

        // TODO: Troy make this private?
        /// <summary>
        /// The state logic for comparing state
        /// </summary>
        public InteractableStates StateManager;

        /// <summary>
        /// Which action is this interactable listening for
        /// </summary>
        public MixedRealityInputAction InputAction;

        /// <summary>
        /// The id of the selected inputAction, for serialization
        /// </summary>
        [HideInInspector]
        [SerializeField]
        private int InputActionId = -1;

        [FormerlySerializedAs("IsGlobal")]
        [SerializeField]
        protected bool isGlobal = false;
        /// <summary>
        /// Is the interactable listening to global events (input only)
        /// </summary>
        public bool IsGlobal
        {
            get { return isGlobal; }
            set
            {
                if (isGlobal != value)
                {
                    isGlobal = value;

                    // If we are active, then change global speech registeration. 
                    // Register handle if we do not require focus, unregister otherwise
                    if (gameObject.activeInHierarchy)
                    {
                        RegisterHandler<IMixedRealityInputHandler>(isGlobal);
                    }
                }
            }
        }

        [FormerlySerializedAs("Dimensions")]
        [SerializeField]
        protected int dimensions = 1;
        /// <summary>
        /// A way of adding more layers of states for controls like toggles
        /// </summary>
        public int Dimensions
        {
            get { return dimensions; }
            set
            {
                // Value cannot be negative or zero
                if (value > 0)
                {
                    dimensions = value;

                    CurrentDimension = Mathf.Clamp(CurrentDimension, 0, Dimensions - 1);
                }
            }
        }

        // cache of current dimension
        [SerializeField]
        protected int dimensionIndex = 0;
        /// <summary>
        /// Current Dimension index based zero and must be less than Dimensions
        /// </summary>
        public int CurrentDimension
        {
            get { return dimensionIndex; }
            set
            {
                // If valid value and not our current value, then update
                if (value >= 0 && value < Dimensions && dimensionIndex != value)
                {
                    dimensionIndex = value;

                    // If we are in toggle mode, update IsToggled state based on current dimension
                    // This needs to happen after updating dimensionIndex, since IsToggled.set will call CurrentDimension.set again
                    if (ButtonMode == SelectionModes.Toggle)
                    {
                        IsToggled = dimensionIndex > 0;
                    }

                    SetupThemes();
                    forceUpdate = true;
                }
            }
        }

        /// <summary>
        /// Returns the current selection mode of the Interactable based on the number of Dimensions available
        /// </summary>
        /// <remarks>
        /// Returns the following under the associated conditions:
        /// SelectionModes.Button => Dimensions == 1
        /// SelectionModes.Toggle => Dimensions == 2
        /// SelectionModes.MultiDimension => Dimensions > 2
        /// </remarks>
        public SelectionModes ButtonMode
        {
            get
            {
                if (Dimensions == 1)
                {
                    return SelectionModes.Button;
                }
                else if (Dimensions == 2)
                {
                    return SelectionModes.Toggle;
                }
                else
                {
                    return SelectionModes.MultiDimension;
                }
            }
        }

        /// <summary>
        /// The Dimension value to set on start
        /// </summary>
        [SerializeField]
        private int StartDimensionIndex = 0;

        /// <summary>
        /// Is the interactive selectable?
        /// When a multi-dimension button, can the user initiate switching dimensions?
        /// </summary>
        public bool CanSelect = true;

        /// <summary>
        /// Can the user deselect a toggle?
        /// A radial button or tab should set this to false
        /// </summary>
        public bool CanDeselect = true;

        /// <summary>
        /// A voice command to fire a click event
        /// </summary>
        public string VoiceCommand = "";

        [FormerlySerializedAs("RequiresFocus")]
        [SerializeField]
        public bool voiceRequiresFocus = true;
        /// <summary>
        /// Does the voice command require this to have focus?
        /// Registers as a global listener for speech commands, ignores input events
        /// </summary>
        public bool VoiceRequiresFocus
        {
            get { return voiceRequiresFocus; }
            set
            {
                if (voiceRequiresFocus != value)
                {
                    voiceRequiresFocus = value;

                    // If we are active, then change global speech registeration. 
                    // Register handle if we do not require focus, unregister otherwise
                    if (gameObject.activeInHierarchy)
                    {
                        RegisterHandler<IMixedRealitySpeechHandler>(!voiceRequiresFocus);
                    }
                }
            }
        }

        [FormerlySerializedAs("Profiles")]
        [SerializeField]
        private List<InteractableProfileItem> profiles = new List<InteractableProfileItem>();
        /// <summary>
        /// List of profiles can match themes with gameObjects
        /// </summary>
        public List<InteractableProfileItem> Profiles
        {
            get { return profiles; }
            set
            {
                profiles = value;
                SetupThemes();
            }
        }

        /// <summary>
        /// Base onclick event
        /// </summary>
        public UnityEvent OnClick = new UnityEvent();

        [SerializeField]
        private List<InteractableEvent> Events = new List<InteractableEvent>();
        /// <summary>
        /// List of events added to this interactable
        /// </summary>
        public List<InteractableEvent> InteractableEvents
        {
            get { return Events; }
            set
            {
                Events = value;
                SetupEvents();
            }
        }

        /// <summary>
        /// The list of running theme instances to receive state changes
        /// When the dimension index changes, the list of themes that are updated changes to those assigned to that dimension.
        /// </summary>
        private List<InteractableThemeBase> activeThemes = new List<InteractableThemeBase>();

        /// <summary>
        /// How many times this interactable was clicked
        /// </summary>
        /// <remarks>
        /// Useful for checking when a click event occurs.
        /// </remarks>
        public int ClickCount { get; private set; }

        #region States

        // TODO: Troy - add serialized field just for inspector and to convert?, but this is also editable in inspector***
        [FormerlySerializedAs("Enabled")]
        [SerializeField]
        private bool startEnabled = false;

        /// <summary>
        /// Is the interactable enabled?
        /// TODO: TROY - Update comment here to be better
        /// </summary>
        public virtual bool IsEnabled
        {
            // Note the inverse setting since targeting "Disable" state but property is concerning "Enabled"
            get { return !(GetStateValue(InteractableStates.InteractableStateEnum.Disabled) > 0); }
            set
            {
                // TODO: DO STUFF HERE!!!
                SetState(InteractableStates.InteractableStateEnum.Disabled, !value);
            }
        }

        /// <summary>
        /// Has focus
        /// </summary>
        public virtual bool HasFocus
        {
            get { return GetStateValue(InteractableStates.InteractableStateEnum.Focus) > 0; }
            set
            {
                if (!value && HasPress)
                {
                    rollOffTimer = 0;
                }
                else
                {
                    rollOffTimer = rollOffTime;
                }

                SetState(InteractableStates.InteractableStateEnum.Focus, value);
            }
        }

        /// <summary>
        /// Currently being pressed
        /// </summary>
        public virtual bool HasPress
        {
            get { return GetStateValue(InteractableStates.InteractableStateEnum.Pressed) > 0; }
            set { SetState(InteractableStates.InteractableStateEnum.Pressed, value); }
        }

        /// <summary>
        /// TODO: TROY - Update better comments
        /// Has focus, finger up - custom: not set by Interactable
        /// </summary>
        public virtual bool IsTargeted
        {
            get { return GetStateValue(InteractableStates.InteractableStateEnum.Targeted) > 0; }
            set { SetState(InteractableStates.InteractableStateEnum.Targeted, value); }
        }

        /// <summary>
        /// No focus, finger is up - custom: not set by Interactable
        /// </summary>
        public virtual bool IsInteractive
        {
            get { return GetStateValue(InteractableStates.InteractableStateEnum.Interactive) > 0; }
            set { SetState(InteractableStates.InteractableStateEnum.Interactive, value); }
        }

        /// <summary>
        /// Has focus, finger down - custom: not set by Interactable
        /// </summary>
        public virtual bool HasObservationTargeted
        {
            get { return GetStateValue(InteractableStates.InteractableStateEnum.ObservationTargeted) > 0; }
            set { SetState(InteractableStates.InteractableStateEnum.ObservationTargeted, value); }
        }

        /// <summary>
        /// No focus, finger down - custom: not set by Interactable
        /// </summary>
        public virtual bool HasObservation
        {
            get { return GetStateValue(InteractableStates.InteractableStateEnum.Observation) > 0; }
            set { SetState(InteractableStates.InteractableStateEnum.Observation, value); }
        }

        /// <summary>
        /// The Interactable has been clicked
        /// </summary>
        public virtual bool IsVisited
        {
            get { return GetStateValue(InteractableStates.InteractableStateEnum.Visited) > 0; }
            set { SetState(InteractableStates.InteractableStateEnum.Visited, value); }
        }

        /// <summary>
        /// TODO: Troy - revisit
        /// True if SelectionMode is "Toggle" (Dimensions == 2) and the dimension index is not zero.
        /// </summary>
        public virtual bool IsToggled
        {
            get
            {
                return GetStateValue(InteractableStates.InteractableStateEnum.Toggled) > 0;
                //return Dimensions == 2 && dimensionIndex > 0;
            }
            set
            {
                // TODO: Troy revisit
                // if in toggle mode
                if (ButtonMode == SelectionModes.Toggle)
                {
                    SetState(InteractableStates.InteractableStateEnum.Toggled, value);

                    CurrentDimension = value ? 1 : 0;
                }
                else
                {
                    Debug.Log($"SetToggled(bool) called, but SelectionMode is set to {ButtonMode}, so Current Dimension was unchanged.");
                }
            }
        }

        /// <summary>
        /// Currently pressed and some movement has occurred
        /// </summary>
        public virtual bool HasGesture
        {
            get { return GetStateValue(InteractableStates.InteractableStateEnum.Gesture) > 0; }
            set { SetState(InteractableStates.InteractableStateEnum.Gesture, value); }
        }

        /// <summary>
        /// Gesture reached max threshold or limits - custom: not set by Interactable
        /// </summary>
        public virtual bool HasGestureMax
        {
            get { return GetStateValue(InteractableStates.InteractableStateEnum.GestureMax) > 0; }
            set { SetState(InteractableStates.InteractableStateEnum.GestureMax, value); }
        }

        /// <summary>
        /// Interactable is touching another object - custom: not set by Interactable
        /// </summary>
        public virtual bool HasCollision
        {
            get { return GetStateValue(InteractableStates.InteractableStateEnum.Collision) > 0; }
            set { SetState(InteractableStates.InteractableStateEnum.Collision, value); }
        }

        /// <summary>
        /// A voice command has occurred, this does not automatically reset
        /// </summary>
        public virtual bool HasVoiceCommand
        {
            get { return GetStateValue(InteractableStates.InteractableStateEnum.VoiceCommand) > 0; }
            set { SetState(InteractableStates.InteractableStateEnum.VoiceCommand, value); }
        }

        /// <summary>
        /// A near interaction touchable is actively being touched
        /// </summary>
        public virtual bool HasPhysicalTouch
        {
            get { return GetStateValue(InteractableStates.InteractableStateEnum.PhysicalTouch) > 0; }
            set { SetState(InteractableStates.InteractableStateEnum.PhysicalTouch, value); }
        }

        /// <summary>
        /// Misc - custom: not set by Interactable
        /// </summary>
        public virtual bool HasCustom
        {
            get { return GetStateValue(InteractableStates.InteractableStateEnum.Custom) > 0; }
            set { SetState(InteractableStates.InteractableStateEnum.Custom, value); }
        }

        /// <summary>
        /// A near interaction grabbable is actively being grabbed/
        /// </summary>
        public virtual bool HasGrab
        {
            get { return GetStateValue(InteractableStates.InteractableStateEnum.Grab) > 0; }
            set { SetState(InteractableStates.InteractableStateEnum.Grab, value); }
        }

        #endregion

        protected State lastState;

        // directly manipulate a theme value, skip blending
        protected bool forceUpdate = false;

        // allows for switching colliders without firing a lose focus immediately
        // for advanced controls like drop-downs
        protected float rollOffTime = 0.25f;
        protected float rollOffTimer = 0.25f;

        protected string[] voiceCommands;

        protected List<IInteractableHandler> handlers = new List<IInteractableHandler>();

        protected Coroutine globalTimer;

        // A click must occur within this many seconds after an input down
        protected float clickTime = 1.5f;
        protected Coroutine clickValidTimer;
        protected float globalFeedbackClickTime = 0.3f;

        #region Gesture State Variables

        /// <summary>
        /// The position of the controller when input down occurs.
        /// Used to determine when controller has moved far enough to trigger gesture
        /// </summary>
        protected Vector3? dragStartPosition = null;

        // Input must move at least this distance before a gesture is considered started, for 2D input like thumbstick
        static readonly float gestureStartThresholdVector2 = 0.1f;

        // Input must move at least this distance before a gesture is considered started, for 3D input
        static readonly float gestureStartThresholdVector3 = 0.05f;

        // Input must move at least this distance before a gesture is considered started, for
        // mixed reality pose input. This is the distance and hand or controller needs to move
        static readonly float gestureStartThresholdMixedRealityPose = 0.1f;

        #endregion

        #region MonoBehaviorImplementation

        protected virtual void Awake()
        {
            if (States == null)
            {
                States = GetDefaultInteractableStates();
            }

            // TODO: Troy temp?
            IsEnabled = startEnabled;

            InputAction = ResolveInputAction(InputActionId);

            CurrentDimension = StartDimensionIndex;

            RefreshSetup();
        }

        protected virtual void OnEnable()
        {
            if (!VoiceRequiresFocus)
            {
                RegisterHandler<IMixedRealitySpeechHandler>(true);
            }

            if (IsGlobal)
            {
                RegisterHandler<IMixedRealityInputHandler>(true);
            }

            focusingPointers.RemoveAll((focusingPointer) => (focusingPointer.FocusTarget as Interactable) != this);

            if (focusingPointers.Count == 0)
            {
                ResetBaseStates();
                RefreshSetup();
            }
        }

        protected virtual void OnDisable()
        {
            // If we registered to receive global events, remove ourselves when disabled
            if (!VoiceRequiresFocus)
            {
                RegisterHandler<IMixedRealitySpeechHandler>(false);
            }

            if (IsGlobal)
            {
                RegisterHandler<IMixedRealityInputHandler>(false);
            }
        }

        // TODO: Troy necessary??
        protected virtual void Start()
        {
            InternalUpdate();
        }

        protected virtual void Update()
        {
            InternalUpdate();
        }

        private void InternalUpdate()
        {
            if (rollOffTimer < rollOffTime && HasPress)
            {
                rollOffTimer += Time.deltaTime;

                if (rollOffTimer >= rollOffTime)
                {
                    HasPress = false;
                }
            }

            for (int i = 0; i < InteractableEvents.Count; i++)
            {
                if (InteractableEvents[i].Receiver != null)
                {
                    InteractableEvents[i].Receiver.OnUpdate(StateManager, this);
                }
            }

            for (int i = 0; i < activeThemes.Count; i++)
            {
                if (activeThemes[i].Loaded)
                {
                    activeThemes[i].OnUpdate(StateManager.CurrentState().ActiveIndex, forceUpdate);
                }
            }

            if (lastState != StateManager.CurrentState())
            {
                for (int i = 0; i < handlers.Count; i++)
                {
                    if (handlers[i] != null)
                    {
                        handlers[i].OnStateChange(StateManager, this);
                    }
                }
            }

            if (forceUpdate)
            {
                forceUpdate = false;
            }

            lastState = StateManager.CurrentState();
        }

        #endregion MonoBehavior Implimentation

        #region Interactable Initiation

        /// <summary>
        /// Force re-initialization of Interactable from events, themes and state references
        /// </summary>
        public void RefreshSetup()
        {
            SetupEvents();
            SetupThemes();
            SetupStates();
        }

        /// <summary>
        /// starts the StateManager
        /// </summary>
        protected virtual void SetupStates()
        {
            // TODO: Troy
            // Note that statemanager will clear states but need to update local related properties*
            ResetAllStates();

            Debug.Assert(typeof(InteractableStates).IsAssignableFrom(States.StateModelType), $"Invalid state model of type {States.StateModelType}. State model must extend from {typeof(InteractableStates)}");
            StateManager = (InteractableStates)States.CreateStateModel();
        }

        /// <summary>
        /// Creates the event receiver instances from the Events list
        /// </summary>
        protected virtual void SetupEvents()
        {
            for (int i = 0; i < InteractableEvents.Count; i++)
            {
                InteractableEvents[i].Receiver = InteractableEvent.CreateReceiver(InteractableEvents[i]);
                InteractableEvents[i].Receiver.Host = this;
            }
        }

        /// <summary>
        /// Creates the list of theme instances based on all the theme settings
        /// Themes will be created for the current dimension index
        /// </summary>
        protected virtual void SetupThemes()
        {
            activeThemes.Clear();

            // Profiles are one per GameObject/ThemeContainer
            // ThemeContainers are one per dimension
            // ThemeDefinitions are one per desired effect (i.e theme)
            foreach (var profile in Profiles)
            {
                if (profile.Target != null && profile.Themes != null)
                {
                    if (CurrentDimension >= 0 && CurrentDimension < profile.Themes.Count)
                    {
                        var themeContainer = profile.Themes[CurrentDimension];
                        if (themeContainer.States.Equals(States))
                        {
                            foreach (var themeDefinition in themeContainer.Definitions)
                            {
                                activeThemes.Add(InteractableThemeBase.CreateAndInitTheme(themeDefinition, profile.Target));
                            }
                        }
                        else
                        {
                            Debug.LogWarning($"Could not use {themeContainer.name} in Interactable on {gameObject.name} because Theme's States does not match {States.name}");
                        }
                    }
                }
            }
        }

        #endregion Interactable Initiation

        #region State Utilities

        /// <summary>
        /// Grabs the state value index, returns -1 if no StateManager available
        /// </summary>
        public int GetStateValue(InteractableStates.InteractableStateEnum state)
        {
            if (StateManager != null)
            {
                return StateManager.GetStateValue((int)state);
            }

            return -1;
        }

        /// <summary>
        /// a public way to set state directly
        /// </summary>
        public void SetState(InteractableStates.InteractableStateEnum state, bool value)
        {
            if (StateManager != null)
            {
                StateManager.SetStateValue(state, value ? 1 : 0);
                UpdateState();
            }
        }

        /// <summary>
        /// runs the state logic and sets state based on the current state values
        /// </summary>
        protected virtual void UpdateState()
        {
            StateManager.CompareStates();
        }

        /// <summary>
        /// Reset the basic interaction states
        /// </summary>
        public void ResetBaseStates()
        {
            // reset states
            HasFocus = false;
            HasPress = false;
            HasPhysicalTouch = false;
            HasGrab = false;
            HasGesture = false;
            HasGestureMax = false;
            HasVoiceCommand = false;

            if (globalTimer != null)
            {
                StopCoroutine(globalTimer);
                globalTimer = null;
            }

            dragStartPosition = null;
        }

        /// <summary>
        /// Reset all states in the Interactable and pointer information
        /// </summary>
        public void ResetAllStates()
        {
            focusingPointers.Clear();
            pressingInputSources.Clear();

            ResetBaseStates();

            // TODO: Troy -> Disable?
            IsEnabled = true;

            HasObservation = false;
            HasObservationTargeted = false;
            IsInteractive = false;
            IsTargeted = false;
            IsToggled = false;
            IsVisited = false;
            HasCollision = false;
            HasCustom = false;
        }

        #endregion State Utilities

        #region Dimensions Utilities

        /// <summary>
        /// Increases the Current Dimension by 1. If at end (i.e Dimensions - 1), then loop around to beginning (i.e 0)
        /// </summary>
        public void IncreaseDimension()
        {
            if (CurrentDimension == Dimensions - 1)
            {
                CurrentDimension = 0;
            }
            else
            {
                CurrentDimension++;
            }
        }

        /// <summary>
        /// Decreases the Current Dimension by 1. If at zero, then loop around to end (i.e Dimensions - 1)
        /// </summary>
        public void DecreaseDimension()
        {
            if (CurrentDimension == 0)
            {
                CurrentDimension = Dimensions - 1;
            }
            else
            {
                CurrentDimension--;
            }
        }

        #endregion Dimensions Utilities

        #region Events

        /// <summary>
        /// Register OnClick extra handlers
        /// </summary>
        public void AddHandler(IInteractableHandler handler)
        {
            if (!handlers.Contains(handler))
            {
                handlers.Add(handler);
            }
        }

        /// <summary>
        /// Remove onClick handlers
        /// </summary>
        public void RemoveHandler(IInteractableHandler handler)
        {
            if (handlers.Contains(handler))
            {
                handlers.Remove(handler);
            }
        }

        /// <summary>
        /// Event receivers can be used to listen for different
        /// events at runtime. This method allows receivers to be dynamically added at runtime.
        /// </summary>
        /// <returns>The new event receiver</returns>
        public T AddReceiver<T>() where T : ReceiverBase, new()
        {
            var interactableEvent = new InteractableEvent();
            var result = new T();
            result.Event = interactableEvent.Event;
            interactableEvent.Receiver = result;
            InteractableEvents.Add(interactableEvent);
            return result;
        }

        /// <summary>
        /// Returns the first receiver of type T on the interactable,
        /// or null if nothing is found.
        /// </summary>
        public T GetReceiver<T>() where T : ReceiverBase
        {
            for (int i = 0; i < InteractableEvents.Count; i++)
            {
                if (InteractableEvents[i] != null && InteractableEvents[i].Receiver is T)
                {
                    return (T)InteractableEvents[i].Receiver;
                }
            }
            return null;
        }

        /// <summary>
        /// Returns all receivers of type T on the interactable.
        /// If nothing is found, returns empty list.
        /// </summary>
        public List<T> GetReceivers<T>() where T : ReceiverBase
        {
            List<T> result = new List<T>();
            for (int i = 0; i < InteractableEvents.Count; i++)
            {
                if (InteractableEvents[i] != null && InteractableEvents[i].Receiver is T)
                {
                    result.Add((T)InteractableEvents[i].Receiver);
                }
            }
            return result;
        }

        #endregion

        #region Input Timers

        /// <summary>
        /// Starts a timer to check if input is in progress
        ///  - Make sure global pointer events are not double firing
        ///  - Make sure Global Input events are not double firing
        ///  - Make sure pointer events are not duplicating an input event
        /// </summary>
        protected void StartClickTimer(bool isFromInputDown = false)
        {
            if (IsGlobal || isFromInputDown)
            {
                if (clickValidTimer != null)
                {
                    StopClickTimer();
                }

                clickValidTimer = StartCoroutine(InputDownTimer(clickTime));
            }
        }

        protected void StopClickTimer()
        {
            Debug.Assert(clickValidTimer != null, "StopClickTimer called but no click timer is running");
            StopCoroutine(clickValidTimer);
            clickValidTimer = null;
        }

        /// <summary>
        /// A timer for the MixedRealityInputHandlers, clicks should occur within a certain time.
        /// </summary>
        protected IEnumerator InputDownTimer(float time)
        {
            yield return new WaitForSeconds(time);
            clickValidTimer = null;
        }

        /// <summary>
        /// Return true if the interactable can fire a click event.
        /// Clicks can only occur within a short duration of an input down firing.
        /// </summary>
        private bool CanFireClick()
        {
            return clickValidTimer != null;
        }

        #endregion

        #region Interactable Utilities

        private void RegisterHandler<T>(bool enable) where T : IEventSystemHandler
        {
            if (enable)
            {
                CoreServices.InputSystem.RegisterHandler<T>(this);
            }
            else
            {
                CoreServices.InputSystem.UnregisterHandler<T>(this);
            }
        }

        /// <summary>
        /// Assigns the InputAction based on the InputActionId
        /// </summary>
        public static MixedRealityInputAction ResolveInputAction(int index)
        {
            MixedRealityInputAction[] actions = CoreServices.InputSystem.InputSystemProfile.InputActionsProfile.InputActions;
            index = Mathf.Clamp(index, 0, actions.Length - 1);
            return actions[index];
        }

        /// <summary>
        /// Based on inputAction and state, should interactable listen to this up/down event.
        /// </summary>
        protected virtual bool ShouldListenToUpDownEvent(InputEventData data)
        {
            if (!(HasFocus || IsGlobal))
            {
                return false;
            }

            if (data.MixedRealityInputAction != InputAction)
            {
                return false;
            }

            // Special case: Make sure that we are not being focused only by a PokePointer, since PokePointer
            // dispatches touch events and should not be dispatching button presses like select, grip, menu, etc.
            int focusingPointerCount = 0;
            int focusingPokePointerCount = 0;
            for (int i = 0; i < focusingPointers.Count; i++)
            {
                if (focusingPointers[i].InputSourceParent.SourceId == data.SourceId)
                {
                    focusingPointerCount++;
                    if (focusingPointers[i] is PokePointer)
                    {
                        focusingPokePointerCount++;
                    }
                }
            }

            if (focusingPointerCount > 0 && focusingPointerCount == focusingPokePointerCount)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Returns true if the inputeventdata is being dispatched from a near pointer
        /// </summary>
        private bool IsInputFromNearInteraction(InputEventData eventData)
        {
            bool isAnyNearpointerFocusing = false;
            for (int i = 0; i < focusingPointers.Count; i++)
            {
                if (focusingPointers[i].InputSourceParent.SourceId == eventData.InputSource.SourceId && focusingPointers[i] is IMixedRealityNearPointer)
                {
                    isAnyNearpointerFocusing = true;
                    break;
                }
            }
            return isAnyNearpointerFocusing;
        }

        /// <summary>
        /// Based on button settings and state, should this button listen to input?
        /// </summary>
        protected virtual bool CanInteract()
        {
            // TODO: Troy clean up code
            if (!IsEnabled)
            {
                return false;
            }

            if (Dimensions > 1 && ((dimensionIndex != Dimensions - 1 && !CanSelect) || (dimensionIndex == Dimensions - 1 && !CanDeselect)))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// A public way to trigger or route an onClick event from an external source, like PressableButton
        /// </summary>
        public void TriggerOnClick()
        {
            IncreaseDimension();

            SendOnClick(null);

            IsVisited = true;
        }

        /// <summary>
        /// Call onClick methods on receivers or IInteractableHandlers
        /// </summary>
        protected void SendOnClick(IMixedRealityPointer pointer)
        {
            OnClick.Invoke();
            ClickCount++;

            for (int i = 0; i < InteractableEvents.Count; i++)
            {
                if (InteractableEvents[i].Receiver != null)
                {
                    InteractableEvents[i].Receiver.OnClick(StateManager, this, pointer);
                }
            }

            for (int i = 0; i < handlers.Count; i++)
            {
                if (handlers[i] != null)
                {
                    handlers[i].OnClick(StateManager, this, pointer);
                }
            }
        }

        /// <summary>
        /// sets some visual states for automating button events like clicks from a keyword
        /// </summary>
        protected void StartGlobalVisual(bool voiceCommand = false)
        {
            // TODO: WHAT??S?D?SD?F?SDF?
            if (voiceCommand)
            {
                StateManager.SetStateValue(InteractableStates.InteractableStateEnum.VoiceCommand, 1);
            }

            IsVisited = true;

            StateManager.SetStateValue(InteractableStates.InteractableStateEnum.Focus, 1);
            StateManager.SetStateValue(InteractableStates.InteractableStateEnum.Pressed, 1);
            UpdateState();

            if (globalTimer != null)
            {
                StopCoroutine(globalTimer);
            }

            globalTimer = StartCoroutine(GlobalVisualReset(globalFeedbackClickTime));
        }

        /// <summary>
        /// Clears up any automated visual states
        /// </summary>
        protected IEnumerator GlobalVisualReset(float time)
        {
            // TODO: WHATFFEWEF
            yield return new WaitForSeconds(time);

            StateManager.SetStateValue(InteractableStates.InteractableStateEnum.VoiceCommand, 0);
            if (!HasFocus)
            {
                StateManager.SetStateValue(InteractableStates.InteractableStateEnum.Focus, 0);
            }

            if (!HasPress)
            {
                StateManager.SetStateValue(InteractableStates.InteractableStateEnum.Pressed, 0);
            }

            UpdateState();

            globalTimer = null;
        }

        /// <summary>
        /// Public method that can be used to set state of interactable
        /// corresponding to an input going down (select button, menu button, touch) 
        /// </summary>
        public void SetInputDown()
        {
            if (!CanInteract())
            {
                return;
            }
            dragStartPosition = null;

            HasPress = true;

            StartClickTimer(true);
        }

        /// <summary>
        /// Public method that can be used to set state of interactable
        /// corresponding to an input going up.
        /// </summary>
        public void SetInputUp()
        {
            if (!CanInteract())
            {
                return;
            }

            HasPress = false;
            HasGesture = false;

            if (CanFireClick())
            {
                StopClickTimer();

                TriggerOnClick();
                IsVisited = true;
            }
        }

        private void OnInputChangedHelper<T>(InputEventData<T> eventData, Vector3 inputPosition, float gestureDeadzoneThreshold)
        {
            if (!CanInteract())
            {
                return;
            }

            if (ShouldListenToMoveEvent(eventData))
            {
                if (dragStartPosition == null)
                {
                    dragStartPosition = inputPosition;
                }
                else if (!HasGesture)
                {
                    if (Vector3.Distance(dragStartPosition.Value, inputPosition) > gestureStartThresholdVector2)
                    {
                        HasGesture = true;
                    }
                }
            }
        }

        private bool ShouldListenToMoveEvent<T>(InputEventData<T> eventData)
        {
            if (!(HasFocus || IsGlobal))
            {
                return false;
            }

            if (!HasPress)
            {
                return false;
            }

            // Ensure that this move event is from a pointer that is pressing the interactable
            int matchingPointerCount = 0;
            foreach (var pressingInputSource in pressingInputSources)
            {
                if (pressingInputSource == eventData.InputSource)
                {
                    matchingPointerCount++;
                }
            }

            return matchingPointerCount > 0;
        }

        /// <summary>
        /// Creates the default States ScriptableObject configured for Interactable
        /// </summary>
        /// <returns>Default Interactable States asset</returns>
        public static States GetDefaultInteractableStates()
        {
            States result = ScriptableObject.CreateInstance<States>();
            InteractableStates allInteractableStates = new InteractableStates();
            result.StateModelType = typeof(InteractableStates);
            result.StateList = allInteractableStates.GetDefaultStates();
            result.DefaultIndex = 0;
            return result;
        }

        /// <summary>
        /// Helper function to create a new Theme asset using Default Interactable States and provided theme definitions
        /// </summary>
        /// <param name="themeDefintions">List of Theme Definitions to associate with Theme asset</param>
        /// <returns>Theme ScriptableObject instance</returns>
        public static Theme GetDefaultThemeAsset(List<ThemeDefinition> themeDefintions)
        {
            // Create the Theme configuration asset
            Theme newTheme = ScriptableObject.CreateInstance<Theme>();
            newTheme.States = GetDefaultInteractableStates();
            newTheme.Definitions = themeDefintions;
            return newTheme;
        }

        #endregion

        #region MixedRealityFocusChangedHandlers

        /// <inheritdoc/>
        public void OnBeforeFocusChange(FocusEventData eventData)
        {
            if (!CanInteract())
            {
                return;
            }

            if (eventData.NewFocusedObject == null)
            {
                focusingPointers.Remove(eventData.Pointer);
            }
            else if (eventData.NewFocusedObject.transform.IsChildOf(gameObject.transform))
            {
                if (!focusingPointers.Contains(eventData.Pointer))
                {
                    focusingPointers.Add(eventData.Pointer);
                }
            }
            else if (eventData.OldFocusedObject.transform.IsChildOf(gameObject.transform))
            {
                focusingPointers.Remove(eventData.Pointer);
            }
        }

        /// <inheritdoc/>
        public void OnFocusChanged(FocusEventData eventData) { }

        #endregion MixedRealityFocusChangedHandlers

        #region MixedRealityFocusHandlers

        /// <inheritdoc/>
        public void OnFocusEnter(FocusEventData eventData)
        {
            if (CanInteract())
            {
                Debug.Assert(focusingPointers.Count > 0,
                    "OnFocusEnter called but focusingPointers == 0. Most likely caused by the presence of a child object " +
                    "that is handling IMixedRealityFocusChangedHandler");

                HasFocus = true;
            }
        }

        /// <inheritdoc/>
        public void OnFocusExit(FocusEventData eventData)
        {
            if (!CanInteract() && !HasFocus)
            {
                return;
            }

            HasFocus = focusingPointers.Count > 0;
        }

        #endregion MixedRealityFocusHandlers

        #region MixedRealityInputHandlers

        /// <inheritdoc/>
        public void OnPositionInputChanged(InputEventData<Vector2> eventData) { }

        #endregion MixedRealityInputHandlers

        #region MixedRealityVoiceCommands

        /// <summary>
        /// Voice commands from MixedRealitySpeechCommandProfile, keyword recognized
        /// </summary>
        public void OnSpeechKeywordRecognized(SpeechEventData eventData)
        {
            if (eventData.Command.Keyword == VoiceCommand && (!VoiceRequiresFocus || HasFocus) && IsEnabled)
            {
                StartGlobalVisual(true);
                HasVoiceCommand = true;
                SendVoiceCommands(VoiceCommand, 0, 1);
                TriggerOnClick();
                eventData.Use();
            }
        }

        /// <summary>
        /// call OnVoinceCommand methods on receivers or IInteractableHandlers
        /// </summary>
        protected void SendVoiceCommands(string command, int index, int length)
        {
            for (int i = 0; i < InteractableEvents.Count; i++)
            {
                if (InteractableEvents[i].Receiver != null)
                {
                    InteractableEvents[i].Receiver.OnVoiceCommand(StateManager, this, command, index, length);
                }
            }

            for (int i = 0; i < handlers.Count; i++)
            {
                if (handlers[i] != null)
                {
                    handlers[i].OnVoiceCommand(StateManager, this, command, index, length);
                }
            }
        }

        /// <summary>
        /// checks the voiceCommand array for a keyword and returns it's index
        /// </summary>
        protected int GetVoiceCommandIndex(string command)
        {
            if (voiceCommands.Length > 1)
            {
                for (int i = 0; i < voiceCommands.Length; i++)
                {
                    if (command == voiceCommands[i])
                    {
                        return i;
                    }
                }
            }

            return 0;
        }

        #endregion VoiceCommands

        #region MixedRealityTouchHandlers

        public void OnTouchStarted(HandTrackingInputEventData eventData)
        {
            HasPress = true;
            HasPhysicalTouch = true;
            eventData.Use();
        }

        public void OnTouchCompleted(HandTrackingInputEventData eventData)
        {
            HasPress = false;
            HasPhysicalTouch = false;
            eventData.Use();
        }

        public void OnTouchUpdated(HandTrackingInputEventData eventData) { }

        #endregion TouchHandlers

        #region MixedRealityInputHandlers

        /// <inheritdoc/>
        public void OnInputUp(InputEventData eventData)
        {
            if (!CanInteract() && !HasPress)
            {
                return;
            }

            if (ShouldListenToUpDownEvent(eventData))
            {
                SetInputUp();
                if (IsInputFromNearInteraction(eventData))
                {
                    // TODO:what if we have two hands grabbing?
                    HasGrab = false;
                }

                eventData.Use();
            }
            pressingInputSources.Remove(eventData.InputSource);
        }

        /// <inheritdoc/>
        public void OnInputDown(InputEventData eventData)
        {
            if (!CanInteract())
            {
                return;
            }

            if (ShouldListenToUpDownEvent(eventData))
            {
                pressingInputSources.Add(eventData.InputSource);
                SetInputDown();
                HasGrab = IsInputFromNearInteraction(eventData);

                eventData.Use();
            }
        }

        /// <inheritdoc/>
        public void OnInputChanged(InputEventData<Vector2> eventData)
        {
            OnInputChangedHelper(eventData, eventData.InputData, gestureStartThresholdVector2);
        }

        /// <inheritdoc/>
        public void OnInputChanged(InputEventData<Vector3> eventData)
        {
            OnInputChangedHelper(eventData, eventData.InputData, gestureStartThresholdVector3);
        }

        /// <inheritdoc/>
        public void OnInputChanged(InputEventData<MixedRealityPose> eventData)
        {
            OnInputChangedHelper(eventData, eventData.InputData.Position, gestureStartThresholdMixedRealityPose);
        }

        #endregion InputHandlers

        #region Deprecated

        /// <summary>
        /// A public way to access the current dimension
        /// </summary>
        [System.Obsolete("Use CurrentDimension property instead")]
        public int GetDimensionIndex()
        {
            return CurrentDimension;
        }

        /// <summary>
        /// a public way to set the dimension index
        /// </summary>
        [System.Obsolete("Use CurrentDimension property instead")]
        public void SetDimensionIndex(int index)
        {
            CurrentDimension = index;
        }

        /// <summary>
        /// Force re-initialization of Interactable from events, themes and state references
        /// </summary>
        [System.Obsolete("Use RefreshSetup() instead")]
        public void ForceUpdateThemes()
        {
            RefreshSetup();
        }

        /// <summary>
        /// Does this interactable require focus
        /// </summary>
        [System.Obsolete("Use IsGlobal instead")]
        public bool FocusEnabled { get { return !IsGlobal; } set { IsGlobal = !value; } }

        /// <summary>
        /// True if Selection is "Toggle" (Dimensions == 2)
        /// </summary>
        [System.Obsolete("Use ButtonMode to test if equal to SelectionModes.Toggle instead")]
        public bool IsToggleButton { get { return Dimensions == 2; } }

        /// <summary>
        /// Is the interactable enabled?
        /// </summary>
        [System.Obsolete("Use IsEnabled instead")]
        public bool Enabled
        {
            get => IsEnabled;
            set => IsEnabled = value;
        }

        /// <summary>
        /// Is disabled
        /// </summary>
        [System.Obsolete("Use IsEnabled instead")]
        public bool IsDisabled
        {
            get => !IsEnabled;
            set => IsEnabled = !value;
        }

        /// <summary>
        /// Returns a list of states assigned to the Interactable
        /// </summary>
        [System.Obsolete("Use States.StateList instead")]
        public State[] GetStates()
        {
            if (States != null)
            {
                return States.StateList.ToArray();
            }

            return new State[0];
        }

        /// <summary>
        /// Handle focus state changes
        /// </summary>
        [System.Obsolete("Use Focus property instead")]
        public virtual void SetFocus(bool focus)
        {
            HasFocus = focus;
        }

        /// <summary>
        /// Change the press state
        /// </summary>
        [System.Obsolete("Use Press property instead")]
        public virtual void SetPress(bool press)
        {
            HasPress = press;
        }

        /// <summary>
        /// Change the disabled state, will override the Enabled property
        /// </summary>
        [System.Obsolete("Use TODO property instead")]
        public virtual void SetDisabled(bool disabled)
        {
            IsEnabled = !disabled;
        }

        /// <summary>
        /// Change the targeted state
        /// </summary>
        [System.Obsolete("Use IsTargeted property instead")]
        public virtual void SetTargeted(bool targeted)
        {
            IsTargeted = targeted;
        }

        /// <summary>
        /// Change the Interactive state
        /// </summary>
        [System.Obsolete("Use IsInteractive property instead")]
        public virtual void SetInteractive(bool interactive)
        {
            IsInteractive = interactive;
        }

        /// <summary>
        /// Change the observation targeted state
        /// </summary>
        [System.Obsolete("Use HasObservationTargeted property instead")]
        public virtual void SetObservationTargeted(bool targeted)
        {
            HasObservationTargeted = targeted;
        }

        /// <summary>
        /// Change the observation state
        /// </summary>
        [System.Obsolete("Use HasObservation property instead")]
        public virtual void SetObservation(bool observation)
        {
            HasObservation = observation;
        }

        /// <summary>
        /// Change the visited state
        /// </summary>
        [System.Obsolete("Use IsVisited property instead")]
        public virtual void SetVisited(bool visited)
        {
            IsVisited = visited;
        }

        /// <summary>
        /// Change the toggled state
        /// </summary>
        [System.Obsolete("Use TODO property instead")]
        public virtual void SetToggled(bool toggled)
        {
            // TODO: Troy
            SetState(InteractableStates.InteractableStateEnum.Toggled, toggled);

            // if in toggle mode
            if (IsToggleButton)
            {
                SetDimensionIndex(toggled ? 1 : 0);
            }
            else
            {
                int selectedMode = Mathf.Clamp(Dimensions, 1, 3);
                Debug.Log("SetToggled(bool) called, but SelectionMode is set to " + (SelectionModes)(selectedMode - 1) + ", so DimensionIndex was unchanged.");
            }
        }

        /// <summary>
        /// Change the gesture state
        /// </summary>
        [System.Obsolete("Use HasGesture property instead")]
        public virtual void SetGesture(bool gesture)
        {
            HasGesture = gesture;
        }

        /// <summary>
        /// Change the gesture max state
        /// </summary>
        [System.Obsolete("Use HasGestureMax property instead")]
        public virtual void SetGestureMax(bool gesture)
        {
            HasGestureMax = gesture;
        }

        /// <summary>
        /// Change the collision state
        /// </summary>
        [System.Obsolete("Use HasCollision property instead")]
        public virtual void SetCollision(bool collision)
        {
            HasCollision = collision;
        }

        /// <summary>
        /// Change the custom state
        /// </summary>
        [System.Obsolete("Use HasCustom property instead")]
        public virtual void SetCustom(bool custom)
        {
            HasCustom = custom;
        }

        /// <summary>
        /// Change the voice command state
        /// </summary>
        [System.Obsolete("Use HasVoiceCommand property instead")]
        public virtual void SetVoiceCommand(bool voice)
        {
            HasVoiceCommand = voice;
        }

        /// <summary>
        /// Change the physical touch state
        /// </summary>
        [System.Obsolete("Use HasPhysicalTouch property instead")]
        public virtual void SetPhysicalTouch(bool touch)
        {
            HasPhysicalTouch = touch;
        }

        /// <summary>
        /// Change the grab state
        /// </summary>
        [System.Obsolete("Use HasGrab property instead")]
        public virtual void SetGrab(bool grab)
        {
            HasGrab = grab;
        }

        #endregion
    }
}
