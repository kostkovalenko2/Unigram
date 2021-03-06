﻿using LinqToVisualTree;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Windows.Input;
using Telegram.Td.Api;
using Template10.Common;
using Unigram.Common;
using Unigram.Common.Chats;
using Unigram.Controls;
using Unigram.Controls.Chats;
using Unigram.Controls.Messages;
using Unigram.Controls.Views;
using Unigram.Converters;
using Unigram.Entities;
using Unigram.Native;
using Unigram.ViewModels;
using Unigram.ViewModels.Delegates;
using Unigram.Views.Chats;
using Unigram.Views.Dialogs;
using Windows.ApplicationModel;
using Windows.ApplicationModel.DataTransfer;
using Windows.Devices.Input;
using Windows.Foundation;
using Windows.Foundation.Metadata;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Composition;
using Windows.UI.Core;
using Windows.UI.Text;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Documents;
using Windows.UI.Xaml.Hosting;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;

namespace Unigram.Views
{
    public sealed partial class ChatPage : Page, INavigablePage, IDialogDelegate, IDisposable
    {
        public DialogViewModel ViewModel => DataContext as DialogViewModel;

        public BindConvert Convert => BindConvert.Current;

        private DialogViewModel _viewModel;
        private double _lastKnownKeyboardHeight = 260;

        private readonly TLWindowContext _windowContext;

        private bool _myPeople;

        private DispatcherTimer _stickersTimer;
        private Visual _stickersPanel;
        private bool _stickersOpen;

        private DispatcherTimer _elapsedTimer;
        private Visual _messageVisual;
        private Visual _ellipseVisual;
        private Visual _elapsedVisual;
        private Visual _slideVisual;
        private Visual _rootVisual;
        private Visual _textShadowVisual;

        private Visual _dateHeaderPanel;
        private Visual _dateHeader;

        private Visual _autocompleteLayer;
        private InsetClip _autocompleteInset;

        private Compositor _compositor;

        public ChatPage()
        {
            InitializeComponent();
            DataContextChanged += (s, args) =>
             {
                 _viewModel = ViewModel;
             };
            DataContext = TLContainer.Current.Resolve<DialogViewModel, IDialogDelegate>(this);
            ViewModel.Sticker_Click = Stickers_ItemClick;

            NavigationCacheMode = NavigationCacheMode.Required;

            _typeToItemHashSetMapping.Add("UserMessageTemplate", new HashSet<SelectorItem>());
            _typeToItemHashSetMapping.Add("ChatFriendMessageTemplate", new HashSet<SelectorItem>());
            _typeToItemHashSetMapping.Add("FriendMessageTemplate", new HashSet<SelectorItem>());
            _typeToItemHashSetMapping.Add("ServiceMessageTemplate", new HashSet<SelectorItem>());
            _typeToItemHashSetMapping.Add("ServiceMessagePhotoTemplate", new HashSet<SelectorItem>());
            _typeToItemHashSetMapping.Add("ServiceMessageUnreadTemplate", new HashSet<SelectorItem>());
            _typeToItemHashSetMapping.Add("EmptyMessageTemplate", new HashSet<SelectorItem>());

            _windowContext = TLWindowContext.GetForCurrentView();

            if (_windowContext.ContactPanel != null)
            {
                _myPeople = true;
                _windowContext.ContactPanel.LaunchFullAppRequested += ContactPanel_LaunchFullAppRequested;

                Header.Visibility = Visibility.Collapsed;
                FindName("BackgroundPresenter");
                BackgroundPresenter.Update(ViewModel.SessionId, ViewModel.Settings, ViewModel.Aggregator);
            }
            else if (!Windows.ApplicationModel.Core.CoreApplication.GetCurrentView().IsMain)
            {
                FindName("BackgroundPresenter");
                BackgroundPresenter.Update(ViewModel.SessionId, ViewModel.Settings, ViewModel.Aggregator);
            }

            ViewModel.TextField = TextField;
            ViewModel.ListField = Messages;

            CheckMessageBoxEmpty();

            ViewModel.PropertyChanged += OnPropertyChanged;

            InitializeStickers();

            Messages.RegisterPropertyChangedCallback(ListViewBase.SelectionModeProperty, List_SelectionModeChanged);
            StickersPanel.RegisterPropertyChangedCallback(FrameworkElement.VisibilityProperty, StickersPanel_VisibilityChanged);

            _messageVisual = ElementCompositionPreview.GetElementVisual(TextField);
            _ellipseVisual = ElementCompositionPreview.GetElementVisual(Ellipse);
            _elapsedVisual = ElementCompositionPreview.GetElementVisual(ElapsedPanel);
            _slideVisual = ElementCompositionPreview.GetElementVisual(SlidePanel);
            _rootVisual = ElementCompositionPreview.GetElementVisual(TextArea);
            _compositor = _slideVisual.Compositor;

            _ellipseVisual.CenterPoint = new Vector3(48);
            _ellipseVisual.Scale = new Vector3(0);

            _rootVisual.Clip = _compositor.CreateInsetClip(0, -100, 0, 0);

            if (DateHeaderPanel != null)
            {
                _dateHeaderPanel = ElementCompositionPreview.GetElementVisual(DateHeaderPanel);
                _dateHeader = ElementCompositionPreview.GetElementVisual(DateHeader);

                _dateHeaderPanel.Clip = _compositor.CreateInsetClip();
            }

            _elapsedTimer = new DispatcherTimer();
            _elapsedTimer.Interval = TimeSpan.FromMilliseconds(100);
            _elapsedTimer.Tick += (s, args) =>
            {
                ElapsedLabel.Text = btnVoiceMessage.Elapsed.ToString("m\\:ss\\.ff");
            };

            var visual = Shadow.Attach(ArrowShadow, 2, 0.25f, null);
            visual.Size = new Vector2(36, 36);
            visual.Offset = new Vector3(0, 1, 0);

            visual = Shadow.Attach(ArrowMentionsShadow, 2, 0.25f, null);
            visual.Size = new Vector2(36, 36);
            visual.Offset = new Vector3(0, 1, 0);

            //if (ApiInformation.IsMethodPresent("Windows.UI.Xaml.Hosting.ElementCompositionPreview", "SetImplicitShowAnimation"))
            //{
            //    var showShowAnimation = Window.Current.Compositor.CreateSpringScalarAnimation();
            //    showShowAnimation.InitialValue = 0;
            //    showShowAnimation.FinalValue = 1;
            //    showShowAnimation.Target = nameof(Visual.Opacity);

            //    var hideHideAnimation = Window.Current.Compositor.CreateSpringScalarAnimation();
            //    hideHideAnimation.InitialValue = 1;
            //    hideHideAnimation.FinalValue = 0;
            //    hideHideAnimation.Target = nameof(Visual.Opacity);

            //    ElementCompositionPreview.SetImplicitShowAnimation(ManagePanel, showShowAnimation);
            //    ElementCompositionPreview.SetImplicitHideAnimation(ManagePanel, hideHideAnimation);
            //}

            _textShadowVisual = Shadow.Attach(Separator, 20, 0.25f);
            _textShadowVisual.IsVisible = false;

            if (ApiInfo.IsFullExperience && ApiInformation.IsEventPresent("Windows.UI.Xaml.Input.FocusManager", "GettingFocus"))
            {
                FocusManager.GettingFocus += (s, args) =>
                {
                    if (args.NewFocusedElement is FrameworkElement element)
                    {
                        bool IsStickersPanel(Uri uri)
                        {
                            var segment = uri?.Segments.LastOrDefault();
                            if (segment == null)
                            {
                                return false;
                            }

                            var paths = new string[] { "StickersView.xaml", "StickersSearchView.xaml", "EmojisView.xaml" };

                            return paths.Contains(segment);
                        }

                        if (_stickersOpen && !IsStickersPanel(element.BaseUri))
                        {
                            Collapse_Click(StickersPanel, null);
                            args.TrySetNewFocusedElement(TextField);
                        }
                    };
                };
            }

            return;

            if (ApiInformation.IsEventPresent("Windows.UI.Xaml.Input.FocusManager", "GettingFocus"))
            {
                FocusManager.GettingFocus += (s, args) =>
                {
                    //#if DEBUG
                    //                    var element = args.NewFocusedElement as FrameworkElement;
                    //                    if (element != null)
                    //                    {
                    //                        ViewModel.LastSeen = Enum.GetName(typeof(FocusInputDeviceKind), args.InputDevice) + ", " + Enum.GetName(typeof(FocusState), args.FocusState) + ": ";


                    //                        //var control = element as Control;
                    //                        //if (control != null)
                    //                        //{
                    //                        //    if (control.FocusState == FocusState.Pointer && control is ChatListViewItem && args.OldFocusedElement is ChatTextBox)
                    //                        //    {
                    //                        //        args.Cancel = true;
                    //                        //    }
                    //                        //}

                    //                        if (element.Name != null)
                    //                        {
                    //                            if (ViewModel.LastSeen != null)
                    //                            {
                    //                                ViewModel.LastSeen += element.Name + ", ";
                    //                            }
                    //                            else
                    //                            {
                    //                                ViewModel.LastSeen = element.Name + ", ";
                    //                            }
                    //                        }

                    //                        ViewModel.LastSeen += element.GetType().FullName;
                    //                    }
                    //                    else
                    //                    {
                    //                        ViewModel.LastSeen = null;
                    //                    }
                    //#endif

                    // We want to apply this behavior when using mouse only
                    if (args.InputDevice != FocusInputDeviceKind.Mouse)
                    {
                        return;
                    }

                    // We don't want to steal focus from text areas/keyboard navigation
                    if (args.FocusState == FocusState.Keyboard || args.NewFocusedElement is TextBox || args.NewFocusedElement is RichEditBox)
                    {
                        return;
                    }

                    // If new focused element supports programmatic focus (so it's a control)
                    // then we can freely steal focus from it
                    if (args.NewFocusedElement is Control)
                    {
                        if (args.FocusState == FocusState.Programmatic && args.OldFocusedElement is ChatTextBox)
                        {
                            args.TryCancel();
                        }
                        else if (args.FocusState == FocusState.Programmatic)
                        {
                            args.TrySetNewFocusedElement(TextField);
                        }
                        else if (args.OldFocusedElement is ChatTextBox)
                        {
                            args.TryCancel();
                        }
                        else if (args.NewFocusedElement is ChatListViewItem)
                        {
                            args.TrySetNewFocusedElement(TextField);
                        }
                    }
                };
            }
        }

        private void InitializeStickers()
        {
            StickersPanel.EmojiClick = Emojis_ItemClick;
            StickersPanel.StickerClick = Stickers_ItemClick;
            StickersPanel.AnimationClick = Animations_ItemClick;

            if (ApiInfo.IsFullExperience)
            {
                _stickersPanel = ElementCompositionPreview.GetElementVisual(StickersPanel.Presenter);

                _stickersTimer = new DispatcherTimer();
                _stickersTimer.Interval = TimeSpan.FromMilliseconds(300);
                _stickersTimer.Tick += (s, args) =>
                {
                    _stickersTimer.Stop();

                    Collapse_Click(StickersPanel, null);
                    TextField.FocusMaybe(FocusState.Keyboard);
                };

                // Not working here
                VisualStateManager.GoToState(this, "FilledState", false);
                VisualStateManager.GoToState(StickersPanel, "FilledState", false);

                ButtonStickers.PointerEntered += Stickers_Click;
                ButtonStickers.PointerExited += StickersPanel_PointerExited;

                StickersPanel.PointerEntered += StickersPanel_PointerEntered;
                StickersPanel.PointerExited += StickersPanel_PointerExited;

                StickersPanel.AllowFocusOnInteraction = true;
            }
            else
            {
                // Not working here
                VisualStateManager.GoToState(this, "NarrowState", false);
                VisualStateManager.GoToState(StickersPanel, "NarrowState", false);

                StickersPanel.AllowFocusOnInteraction = false;
            }
        }

        private void ContactPanel_LaunchFullAppRequested(Windows.ApplicationModel.Contacts.ContactPanel sender, Windows.ApplicationModel.Contacts.ContactPanelLaunchFullAppRequestedEventArgs args)
        {
            sender.ClosePanel();
            args.Handled = true;

            this.BeginOnUIThread(async () =>
            {
                var chat = ViewModel.Chat;
                if (chat == null)
                {
                    return;
                }

                if (chat.Type is ChatTypePrivate privata)
                {
                    var options = new Windows.System.LauncherOptions();
                    options.TargetApplicationPackageFamilyName = Package.Current.Id.FamilyName;

                    await Windows.System.Launcher.LaunchUriAsync(new Uri("ms-contact-profile://meh?ContactRemoteIds=u" + privata.UserId), options);
                }
            });
        }

        private void TextField_LostFocus(object sender, RoutedEventArgs e)
        {
            if (ApiInfo.IsFullExperience)
            {
                return;
            }

            if (StickersPanel.Visibility == Visibility.Visible && TextField.FocusState == FocusState.Unfocused)
            {
                Collapse_Click(StickersPanel, null);

                TextField.FocusMaybe(FocusState.Keyboard);
            }
            else if (ReplyMarkupPanel.Visibility == Visibility.Visible && ButtonMarkup.Visibility == Visibility.Visible && TextField.FocusState == FocusState.Unfocused)
            {
                CollapseMarkup(false);

                TextField.FocusMaybe(FocusState.Keyboard);
            }
        }

        private void StickersPanel_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (_stickersTimer.IsEnabled)
            {
                _stickersTimer.Stop();
            }
        }

        private void StickersPanel_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse)
            {
                _stickersTimer.Start();
            }
            else if (_stickersTimer.IsEnabled)
            {
                _stickersTimer.Stop();
            }
        }

        private void Stickers_Click(object sender, PointerRoutedEventArgs e)
        {
            if (_stickersTimer.IsEnabled)
            {
                _stickersTimer.Stop();
            }

            if (StickersPanel.Visibility == Visibility.Visible)
            {
                return;
            }

            _stickersOpen = true;

            VisualStateManager.GoToState(this, "FilledState", false);
            VisualStateManager.GoToState(StickersPanel, "FilledState", false);

            Focus(FocusState.Programmatic);
            TextField.Focus(FocusState.Programmatic);

            InputPane.GetForCurrentView().TryHide();

            _stickersPanel.Opacity = 0;
            _stickersPanel.Clip = _compositor.CreateInsetClip(48, 48, 0, 0);

            StickersPanel.Visibility = Visibility.Visible;
            StickersPanel.Refresh();

            ViewModel.OpenStickersCommand.Execute(null);

            var opacity = _compositor.CreateScalarKeyFrameAnimation();
            opacity.InsertKeyFrame(0, 0);
            opacity.InsertKeyFrame(1, 1);

            var clip = _compositor.CreateScalarKeyFrameAnimation();
            clip.InsertKeyFrame(0, 48);
            clip.InsertKeyFrame(1, 0);

            _stickersPanel.StopAnimation("Opacity");
            _stickersPanel.Clip.StopAnimation("LeftInset");
            _stickersPanel.Clip.StopAnimation("TopInset");

            _stickersPanel.StartAnimation("Opacity", opacity);
            _stickersPanel.Clip.StartAnimation("LeftInset", clip);
            _stickersPanel.Clip.StartAnimation("TopInset", clip);
        }

        public void Dispose()
        {
            if (ViewModel != null)
            {
                ViewModel.PropertyChanged -= OnPropertyChanged;

                ViewModel.Delegate = null;
                ViewModel.TextField = null;
                ViewModel.ListField = null;

                //Bindings.StopTracking();
            }

            DataContext = TLContainer.Current.Resolve<DialogViewModel, IDialogDelegate>(this);

            ViewModel.TextField = TextField;
            ViewModel.ListField = Messages;
            ViewModel.Sticker_Click = Stickers_ItemClick;

            ViewModel.SetText(null, false);

            CheckMessageBoxEmpty();

            SearchMask.Update(ViewModel.Search);

            ViewModel.PropertyChanged += OnPropertyChanged;
            ViewModel.Items.AttachChanged = OnAttachChanged;
            //Bindings.Update();

            Playback.Update(ViewModel.CacheService, ViewModel.PlaybackService, ViewModel.NavigationService);

            //LosingFocus -= DialogPage_LosingFocus;
            //LosingFocus += DialogPage_LosingFocus;

            //base.OnNavigatedTo(e);

            TextField.FocusMaybe(FocusState.Keyboard);
        }

        //private void DialogPage_LosingFocus(UIElement sender, LosingFocusEventArgs args)
        //{
        //    if (args.NewFocusedElement is DialogListViewItem && args.OldFocusedElement is BubbleTextBox)
        //    {
        //        args.Cancel = true;
        //        args.Handled = true;
        //    }
        //}

        private void OnAttachChanged(IEnumerable<MessageViewModel> items)
        {
            foreach (var message in items)
            {
                if (message == null)
                {
                    continue;
                }

                var container = Messages.ContainerFromItem(message) as SelectorItem;
                if (container == null)
                {
                    continue;
                }

                var content = container.ContentTemplateRoot as FrameworkElement;
                if (content == null)
                {
                    continue;
                }

                if (content is Grid grid)
                {
                    var photo = grid.FindName("Photo") as ProfilePicture;
                    if (photo != null)
                    {
                        photo.Visibility = message.IsLast ? Visibility.Visible : Visibility.Collapsed;
                    }

                    content = grid.FindName("Bubble") as FrameworkElement;
                }
                else if (content is StackPanel panel && !(content is MessageBubble))
                {
                    content = panel.FindName("Service") as FrameworkElement;
                }

                if (content is MessageBubble bubble)
                {
                    bubble.UpdateAttach(message);
                    bubble.UpdateMessageHeader(message);
                }
                else if (content is MessageService service)
                {
                    //service.UpdateAttach(item);
                }
            }
        }

        private void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName.Equals("Reply"))
            {
                CheckMessageBoxEmpty();
            }
            else if (e.PropertyName.Equals("Search"))
            {
                SearchMask.Update(ViewModel.Search);
            }
        }

        private void List_SelectionModeChanged(DependencyObject sender, DependencyProperty dp)
        {
            if (ViewModel.SelectionMode == ListViewSelectionMode.None)
            {
                ManagePanel.Visibility = Visibility.Collapsed;
                InfoPanel.Visibility = Visibility.Visible;

                ViewModel.SelectedItems = new List<MessageViewModel>();
            }
            else
            {
                ManagePanel.Visibility = Visibility.Visible;
                InfoPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void Manage_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.SelectionMode == ListViewSelectionMode.None)
            {
                ViewModel.SelectionMode = ListViewSelectionMode.Multiple;
            }
            else
            {
                ViewModel.SelectionMode = ListViewSelectionMode.None;
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Bindings.StopTracking();
            Bindings.Update();

            InputPane.GetForCurrentView().Showing += InputPane_Showing;
            InputPane.GetForCurrentView().Hiding += InputPane_Hiding;

            Window.Current.Activated += Window_Activated;
            Window.Current.VisibilityChanged += Window_VisibilityChanged;
            Window.Current.SizeChanged += Window_SizeChanged;

            WindowContext.GetForCurrentView().AcceleratorKeyActivated += Dispatcher_AcceleratorKeyActivated;

            OnSizeChanged(Window.Current.Bounds.Width);

            UnloadVisibleMessages();
            ViewVisibleMessages(false);



            TextField.FocusMaybe(FocusState.Keyboard);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            Bindings.StopTracking();

            UnloadVisibleMessages();

            InputPane.GetForCurrentView().Showing -= InputPane_Showing;
            InputPane.GetForCurrentView().Hiding -= InputPane_Hiding;

            Window.Current.Activated -= Window_Activated;
            Window.Current.VisibilityChanged -= Window_VisibilityChanged;
            Window.Current.SizeChanged -= Window_SizeChanged;

            WindowContext.GetForCurrentView().AcceleratorKeyActivated -= Dispatcher_AcceleratorKeyActivated;
        }

        private void InputPane_Showing(InputPane sender, InputPaneVisibilityEventArgs args)
        {
            args.EnsuredFocusedElementInView = true;
            KeyboardPlaceholder.Height = new GridLength(args.OccludedRect.Height);
            StickersPanel.Height = args.OccludedRect.Height;
            ReplyMarkupPanel.MaxHeight = args.OccludedRect.Height;
            //ReplyMarkupViewer.MaxHeight = args.OccludedRect.Height;

            _lastKnownKeyboardHeight = Math.Max(260, args.OccludedRect.Height);

            Collapse_Click(null, null);
            CollapseMarkup(false);
        }

        private void InputPane_Hiding(InputPane sender, InputPaneVisibilityEventArgs args)
        {
            args.EnsuredFocusedElementInView = true;
            KeyboardPlaceholder.Height = new GridLength(1, GridUnitType.Auto);
        }

        private void Window_Activated(object sender, WindowActivatedEventArgs e)
        {
            if (e.WindowActivationState != CoreWindowActivationState.Deactivated)
            {
                ViewVisibleMessages(false);
                TextField.FocusMaybe(FocusState.Keyboard);
            }
        }

        private void Window_VisibilityChanged(object sender, VisibilityChangedEventArgs e)
        {
            if (e.Visible)
            {
                TextField.FocusMaybe(FocusState.Keyboard);
            }
        }

        private void Window_SizeChanged(object sender, WindowSizeChangedEventArgs e)
        {
            OnSizeChanged(e.Size.Width);
        }

        private void OnSizeChanged(double width)
        {
            AttachRecent.MaxWidth = AttachRestriction.MaxWidth = width < 500 ? width - 16 - 2 : 360;
            AttachRecent.MinWidth = AttachRestriction.MinWidth = width < 500 ? width - 16 - 2 : 360;
        }

        private void Dispatcher_AcceleratorKeyActivated(CoreDispatcher sender, AcceleratorKeyEventArgs args)
        {
            if (args.EventType != CoreAcceleratorKeyEventType.KeyDown && args.EventType != CoreAcceleratorKeyEventType.SystemKeyDown)
            {
                return;
            }

            var ctrl = Window.Current.CoreWindow.GetKeyState(Windows.System.VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);

            if (args.VirtualKey == Windows.System.VirtualKey.Search || (args.VirtualKey == Windows.System.VirtualKey.F && ctrl))
            {
                ViewModel.SearchCommand.Execute();
                args.Handled = true;
            }
            else if (args.VirtualKey == Windows.System.VirtualKey.Delete && ViewModel.SelectionMode != ListViewSelectionMode.None && ViewModel.SelectedItems != null && ViewModel.SelectedItems.Count > 0)
            {
                ViewModel.MessagesDeleteCommand.Execute();
                args.Handled = true;
            }
        }

        public void OnBackRequested(HandledEventArgs args)
        {
            if (ViewModel.Search != null)
            {
                args.Handled = SearchMask.OnBackRequested();
            }

            if (StickersPanel.Visibility == Visibility.Visible)
            {
                if (StickersPanel.ToggleActiveView())
                {

                }
                else
                {
                    Collapse_Click(null, null);
                }

                args.Handled = true;
            }

            if (ReplyMarkupPanel.Visibility == Visibility.Visible && ButtonMarkup.Visibility == Visibility.Visible)
            {
                CollapseMarkup(false);
                args.Handled = true;
            }

            if (ViewModel.SelectionMode != ListViewSelectionMode.None)
            {
                ViewModel.SelectionMode = ListViewSelectionMode.None;
                args.Handled = true;
            }

            if (ViewModel.ComposerHeader != null && ViewModel.ComposerHeader.EditingMessage != null)
            {
                ViewModel.ClearReplyCommand.Execute(null);
                args.Handled = true;
            }

            if (args.Handled)
            {
                Focus(FocusState.Programmatic);
                TextField.FocusMaybe(FocusState.Keyboard);
            }
        }

        //private bool _isAlreadyLoading;
        //private bool _isAlreadyCalled;

        private void CheckMessageBoxEmpty()
        {
            if (StickersPanel.Visibility == Visibility.Visible)
            {
                Collapse_Click(StickersPanel, null);
            }

            if (ReplyMarkupPanel.Visibility == Visibility.Visible && ButtonMarkup.Visibility == Visibility.Visible)
            {
                CollapseMarkup(false);
            }

            if (ViewModel != null && TextField.IsEmpty)
            {
                btnSendMessage.Visibility = Visibility.Collapsed;
                btnCommands.Visibility = Visibility.Visible;
                btnMarkup.Visibility = Visibility.Visible;
                //btnStickers.Visibility = Visibility.Visible;
                btnVoiceMessage.Visibility = Visibility.Visible;

                ButtonStickers.Glyph = "\uE606";
                ButtonStickers.FontFamily = new FontFamily("ms-appx:///Assets/Fonts/Telegram.ttf#Telegram");

                ViewModel.DisableWebPagePreview = false;
            }
            else
            {
                btnSendMessage.Visibility = Visibility.Visible;
                btnCommands.Visibility = Visibility.Collapsed;
                btnMarkup.Visibility = Visibility.Collapsed;
                //btnStickers.Visibility = Visibility.Collapsed;
                btnVoiceMessage.Visibility = Visibility.Collapsed;

                ButtonStickers.Glyph = "\uE76E";
                ButtonStickers.FontFamily = new FontFamily("Segoe MDL2 Assets");
            }

            if (ViewModel.DisableWebPagePreview)
            {
                return;
            }

            var text = ViewModel.GetText(TextGetOptions.None);
            var embedded = ViewModel.ComposerHeader;

            var response = ViewModel.ProtoService.Execute(new GetTextEntities(text));
            if (response is TextEntities entities)
            {
                var entity = entities.Entities.FirstOrDefault(x => x.Type is TextEntityTypeUrl);
                if (entity != null)
                {
                    var address = text.Substring(entity.Offset, entity.Length);
                    if (address.Equals(embedded?.WebPageUrl, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    ViewModel.ProtoService.Send(new GetWebPagePreview(new FormattedText(address, new TextEntity[0])), result =>
                    {
                        this.BeginOnUIThread(() =>
                        {
                            if (!string.Equals(text, ViewModel.GetText(TextGetOptions.None)))
                            {
                                return;
                            }

                            if (result is WebPage webPage)
                            {
                                if (embedded == null)
                                {
                                    ViewModel.ComposerHeader = new MessageComposerHeader { WebPagePreview = webPage, WebPageUrl = address };
                                }
                                else
                                {
                                    ViewModel.ComposerHeader = new MessageComposerHeader { EditingMessage = embedded.EditingMessage, ReplyToMessage = embedded.ReplyToMessage, WebPagePreview = webPage, WebPageUrl = address };
                                }
                            }
                            else if (embedded != null)
                            {
                                if (embedded.IsEmpty)
                                {
                                    ViewModel.ComposerHeader = null;
                                }
                                else if (embedded.WebPagePreview != null)
                                {
                                    ViewModel.ComposerHeader = new MessageComposerHeader { EditingMessage = embedded.EditingMessage, ReplyToMessage = embedded.ReplyToMessage, WebPagePreview = null };
                                }
                            }
                        });
                    });
                }
                else if (embedded != null)
                {
                    if (embedded.IsEmpty)
                    {
                        ViewModel.ComposerHeader = null;
                    }
                    else if (embedded.WebPagePreview != null)
                    {
                        ViewModel.ComposerHeader = new MessageComposerHeader { EditingMessage = embedded.EditingMessage, ReplyToMessage = embedded.ReplyToMessage, WebPagePreview = null };
                    }
                }
            }
        }

        private void TextField_TextChanging(RichEditBox sender, RichEditBoxTextChangingEventArgs args)
        {
            CheckMessageBoxEmpty();
        }

        private void TextField_TextChanged(object sender, RoutedEventArgs e)
        {
            CheckMessageBoxEmpty();
        }

        private async void btnSendMessage_Click(object sender, RoutedEventArgs e)
        {
            await TextField.SendAsync();
        }

        private void Profile_Click(object sender, RoutedEventArgs e)
        {
            var chat = ViewModel.Chat;
            if (chat == null)
            {
                return;
            }

            if (chat.Type is ChatTypePrivate privata && privata.UserId == ViewModel.CacheService.Options.MyId)
            {
                ViewModel.NavigationService.Navigate(typeof(ChatSharedMediaPage), chat.Id, infoOverride: ApiInfo.CanUseDirectComposition ? new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight } : null);
            }
            else
            {
                ViewModel.NavigationService.Navigate(typeof(ProfilePage), chat.Id, infoOverride: ApiInfo.CanUseDirectComposition ? new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight } : null);
            }
        }

        private async void Attach_Click(object sender, RoutedEventArgs e)
        {
            var chat = ViewModel.Chat;
            if (chat == null)
            {
                return;
            }

            //var restricted = await ViewModel.VerifyRightsAsync(chat, x => x.CanSendMediaMessages, Strings.Resources.AttachMediaRestrictedForever, Strings.Resources.AttachMediaRestricted);
            //if (restricted)
            //{
            //    return;
            //}

            var pane = InputPane.GetForCurrentView();
            if (pane.OccludedRect != Rect.Empty)
            {
                pane.TryHide();

                // TODO: Can't find any better solution
                await Task.Delay(200);
            }

            foreach (var item in ViewModel.MediaLibrary)
            {
                item.Reset();
            }

            if (FlyoutBase.GetAttachedFlyout(ButtonAttach) is MenuFlyout flyout)
            {
                //var bounds = ApplicationView.GetForCurrentView().VisibleBounds;
                //if (AnalyticsInfo.VersionInfo.DeviceFamily == "Windows.Mobile" && (bounds.Width < 500 || bounds.Height < 500))
                //{
                //    flyout.LightDismissOverlayMode = LightDismissOverlayMode.On;
                //}
                //else
                //{
                //    flyout.LightDismissOverlayMode = LightDismissOverlayMode.Auto;
                //}

                //flyout.ShowAt(ButtonAttach, new Point(4, -4));
                flyout.ShowAt(FlyoutArea);
            }
        }

        private void AttachPickerFlyout_ItemClick(object sender, MediaSelectedEventArgs e)
        {
            var flyout = FlyoutBase.GetAttachedFlyout(ButtonAttach) as MenuFlyout;
            if (flyout != null)
            {
                flyout.Hide();
            }

            if (e.IsLocal)
            {
                ViewModel.SendMediaExecute(new ObservableCollection<StorageMedia>(ViewModel.MediaLibrary), e.Item);
            }
            else
            {
                ViewModel.SendMediaExecute(new ObservableCollection<StorageMedia> { e.Item }, e.Item);
            }
        }

        private void InlineBotResults_ItemClick(object sender, ItemClickEventArgs e)
        {
            var collection = ViewModel.InlineBotResults;
            if (collection == null)
            {
                return;
            }

            var result = e.ClickedItem as InlineQueryResult;
            if (result == null)
            {
                return;
            }

            ViewModel.SendBotInlineResult(result, collection.GetQueryId(result));
        }

        #region Drag & Drop

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }

        private async void OnDrop(object sender, DragEventArgs e)
        {
            await ViewModel.HandlePackageAsync(e.DataView);
        }
        //gridLoading.Visibility = Visibility.Visible;

        #endregion

        private async void Reply_Click(object sender, RoutedEventArgs e)
        {
            var reference = sender as MessageReference;
            var message = reference.MessageId;

            if (message != 0)
            {
                await ViewModel.LoadMessageSliceAsync(null, message);
            }

            //if (message != null)
            //{
            //    if (message is TLMessagesContainter container)
            //    {
            //        if (container.EditMessage != null)
            //        {
            //            message = container.EditMessage;
            //        }
            //        else
            //        {
            //            return;
            //        }
            //    }

            //    if (message is TLMessageCommonBase messageCommon)
            //    {
            //        await ViewModel.LoadMessageSliceAsync(null, messageCommon.Id);
            //    }
            //}
        }

        private void ReplyMarkup_ButtonClick(object sender, ReplyMarkupButtonClickEventArgs e)
        {
            var panel = sender as ReplyMarkupPanel;
            if (panel != null)
            {
                ViewModel.KeyboardButtonExecute(panel.DataContext as MessageViewModel, e.Button);
            }
        }

        private async void Stickers_Click(object sender, RoutedEventArgs e)
        {
            if (ApiInfo.IsFullExperience)
            {
                Stickers_Click(sender, null as PointerRoutedEventArgs);
                return;
            }

            VisualStateManager.GoToState(this, "NarrowState", false);
            VisualStateManager.GoToState(StickersPanel, "NarrowState", false);

            var chat = ViewModel.Chat;
            if (chat == null)
            {
                return;
            }

            var restricted = await ViewModel.VerifyRightsAsync(chat, x => x.CanSendOtherMessages, Strings.Resources.AttachStickersRestrictedForever, Strings.Resources.AttachStickersRestricted);
            if (restricted)
            {
                return;
            }

            if (StickersPanel.Visibility == Visibility.Collapsed)
            {
                _stickersOpen = true;

                Focus(FocusState.Programmatic);
                TextField.Focus(FocusState.Programmatic);

                InputPane.GetForCurrentView().TryHide();

                StickersPanel.MinHeight = 260;
                StickersPanel.MaxHeight = 360;
                StickersPanel.Height = _lastKnownKeyboardHeight;
                StickersPanel.Visibility = Visibility.Visible;
                StickersPanel.Refresh();

                ViewModel.OpenStickersCommand.Execute(null);
            }
            else
            {
                Focus(FocusState.Programmatic);
                TextField.Focus(FocusState.Keyboard);

                Collapse_Click(StickersPanel, null);
            }
        }

        private void Commands_Click(object sender, RoutedEventArgs e)
        {
            TextField.SetText("/", null);
            TextField.Focus(FocusState.Keyboard);
        }

        private void Markup_Click(object sender, RoutedEventArgs e)
        {
            if (ReplyMarkupPanel.Visibility == Visibility.Visible)
            {
                CollapseMarkup(true);
            }
            else
            {
                ShowMarkup();
            }
        }

        private void CollapseMarkup(bool keyboard)
        {
            ReplyMarkupPanel.Visibility = Visibility.Collapsed;
            ButtonMarkup.IsChecked = false;

            if (keyboard)
            {
                Focus(FocusState.Programmatic);
                TextField.Focus(FocusState.Keyboard);

                InputPane.GetForCurrentView().TryShow();
            }
        }

        public void ShowMarkup()
        {
            ReplyMarkupPanel.Visibility = Visibility.Visible;
            ButtonMarkup.IsChecked = true;

            Focus(FocusState.Programmatic);
            TextField.Focus(FocusState.Programmatic);

            InputPane.GetForCurrentView().TryHide();
        }

        private void TextField_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (StickersPanel.Visibility == Visibility.Visible)
            {
                Collapse_Click(StickersPanel, null);
            }

            if (ReplyMarkupPanel.Visibility == Visibility.Visible && ButtonMarkup.Visibility == Visibility.Visible)
            {
                CollapseMarkup(false);
            }

            InputPane.GetForCurrentView().TryShow();
        }

        private void Photo_Click(object sender, RoutedEventArgs e)
        {
            var control = sender as FrameworkElement;

            var message = control.Tag as MessageViewModel;
            if (message == null)
            {
                return;
            }

            if (message.IsSaved())
            {
                if (message.ForwardInfo is MessageForwardedFromUser fromUser)
                {
                    ViewModel.OpenUser(fromUser.SenderUserId);
                }
                else if (message.ForwardInfo is MessageForwardedPost post)
                {
                    // TODO: verify if this is sufficient
                    ViewModel.OpenChat(post.ChatId);
                }
            }
            else if (message.IsChannelPost)
            {
                ViewModel.OpenChat(message.ChatId);
            }
            else
            {
                ViewModel.OpenUser(message.SenderUserId);
            }
        }

        private void Action_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var message = button.Tag as MessageViewModel;

            if (message == null)
            {
                return;
            }

            if (message.IsSaved())
            {
                if (message.ForwardInfo is MessageForwardedFromUser fromUser)
                {
                    ViewModel.NavigationService.NavigateToChat(fromUser.ForwardedFromChatId, fromUser.ForwardedFromMessageId);
                }
                else if (message.ForwardInfo is MessageForwardedPost post)
                {
                    ViewModel.NavigationService.NavigateToChat(post.ForwardedFromChatId, post.ForwardedFromMessageId);
                }
            }
            else
            {
                ViewModel.MessageShareCommand.Execute(message);
            }
        }

        private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewModel.SelectionMode == ListViewSelectionMode.Multiple)
            {
                ViewModel.ExpandSelection(Messages.SelectedItems.Cast<MessageViewModel>());

                //if (ViewModel.SelectedItems.IsEmpty())
                //{
                //    ViewModel.SelectionMode = ListViewSelectionMode.None;
                //}
            }
        }

        #region Context menu

        private void Menu_ContextRequested(object sender, RoutedEventArgs e)
        {
            var flyout = new MenuFlyout();

            var chat = ViewModel.Chat;
            if (chat == null)
            {
                return;
            }

            //var user = chat.Type is ChatTypePrivate privata ? ViewModel.ProtoService.GetUser(privata.UserId) : null;
            var user = ViewModel.CacheService.GetUser(chat);
            var secret = chat.Type is ChatTypeSecret;
            var basicGroup = chat.Type is ChatTypeBasicGroup basicGroupType ? ViewModel.ProtoService.GetBasicGroup(basicGroupType.BasicGroupId) : null;
            var supergroup = chat.Type is ChatTypeSupergroup supergroupType ? ViewModel.ProtoService.GetSupergroup(supergroupType.SupergroupId) : null;

            flyout.CreateFlyoutItem(ViewModel.SearchCommand, Strings.Resources.Search, new FontIcon { Glyph = Icons.Search }, Windows.System.VirtualKey.F);

            if (supergroup != null && !(supergroup.Status is ChatMemberStatusCreator) && (supergroup.IsChannel || !string.IsNullOrEmpty(supergroup.Username)))
            {
                flyout.CreateFlyoutItem(ViewModel.ReportCommand, Strings.Resources.ReportChat, new FontIcon { Glyph = Icons.Report });
            }
            if (user != null && user.Id != ViewModel.CacheService.Options.MyId)
            {
                if (user.OutgoingLink is LinkStateNone)
                {
                    flyout.CreateFlyoutItem(ViewModel.ShareContactCommand, Strings.Resources.ShareMyContactInfo, new FontIcon { Glyph = Icons.Share });
                }
                else if (user.OutgoingLink is LinkStateKnowsPhoneNumber)
                {
                    flyout.CreateFlyoutItem(ViewModel.AddContactCommand, Strings.Resources.AddToContacts);
                }
            }
            if (secret)
            {
                flyout.CreateFlyoutItem(ViewModel.SetTimerCommand, Strings.Resources.SetTimer, new FontIcon { Glyph = Icons.Timer });
            }
            if (user != null || basicGroup != null || (supergroup != null && !supergroup.IsChannel && string.IsNullOrEmpty(supergroup.Username)))
            {
                flyout.CreateFlyoutItem(ViewModel.DialogClearCommand, Strings.Resources.ClearHistory, new FontIcon { Glyph = Icons.Clear });
            }
            if (user != null)
            {
                flyout.CreateFlyoutItem(ViewModel.DialogDeleteCommand, Strings.Resources.DeleteChatUser, new FontIcon { Glyph = Icons.Delete });
            }
            if (basicGroup != null)
            {
                flyout.CreateFlyoutItem(ViewModel.DialogDeleteCommand, Strings.Resources.DeleteAndExit, new FontIcon { Glyph = Icons.Delete });
            }
            if ((user != null && user.Id != ViewModel.CacheService.Options.MyId) || basicGroup != null || (supergroup != null && !supergroup.IsChannel))
            {
                flyout.CreateFlyoutItem(chat.NotificationSettings.MuteFor == 0 ? ViewModel.MuteCommand : ViewModel.UnmuteCommand, chat.NotificationSettings.MuteFor == 0 ? Strings.Resources.MuteNotifications : Strings.Resources.UnmuteNotifications, new FontIcon { Glyph = chat.NotificationSettings.MuteFor == 0 ? Icons.Mute : Icons.Unmute });
            }

            //if (currentUser == null || !currentUser.IsSelf)
            //{
            //    this.muteItem = this.headerItem.addSubItem(18, null);
            //}
            //else if (currentUser.IsSelf)
            //{
            //    CreateFlyoutItem(ref flyout, null, Strings.Resources.AddShortcut);
            //}
            if (user != null && user.Type is UserTypeBot)
            {
                var fullInfo = ViewModel.ProtoService.GetUserFull(user.Id);
                if (fullInfo != null)
                {
                    if (fullInfo.BotInfo.Commands.Any(x => x.Command.Equals("settings")))
                    {
                        flyout.CreateFlyoutItem(null, Strings.Resources.BotSettings);
                    }

                    if (fullInfo.BotInfo.Commands.Any(x => x.Command.Equals("help")))
                    {
                        flyout.CreateFlyoutItem(null, Strings.Resources.BotHelp);
                    }
                }
            }

            if (flyout.Items.Count > 0)
            {
                flyout.ShowAt((Button)sender);
            }
        }

        private void Message_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
        {
            var flyout = new MenuFlyout();

            var element = sender as FrameworkElement;
            var message = element.Tag as MessageViewModel;
            if (message == null && sender is SelectorItem selector && selector.ContentTemplateRoot is FrameworkElement content)
            {
                element = content;
                message = content.Tag as MessageViewModel;

                if (content is Grid grid)
                {
                    element = grid.FindName("Bubble") as FrameworkElement;
                }
                else if (content is StackPanel panel && !(content is MessageBubble))
                {
                    element = panel.FindName("Service") as FrameworkElement;
                }
            }

            if (message == null || message.Id == 0)
            {
                return;
            }

            var chat = message.GetChat();
            if (chat == null)
            {
                return;
            }

            //if (messageCommon is TLMessageService serviceMessage && (serviceMessage.Action is TLMessageActionDate || serviceMessage.Action is TLMessageActionUnreadMessages))
            //{
            //    return;
            //}

            // Generic
            flyout.CreateFlyoutItem(MessageReply_Loaded, ViewModel.MessageReplyCommand, message, Strings.Resources.Reply, new FontIcon { Glyph = Icons.Reply });
            flyout.CreateFlyoutItem(MessageEdit_Loaded, ViewModel.MessageEditCommand, message, Strings.Resources.Edit, new FontIcon { Glyph = Icons.Edit });

            flyout.CreateFlyoutSeparator();

            // Manage
            if (chat.Type is ChatTypeSupergroup supergroup)
            {
                var fullInfo = ViewModel.CacheService.GetSupergroupFull(supergroup.SupergroupId);
                if (fullInfo != null)
                {
                    flyout.CreateFlyoutItem(MessagePin_Loaded, ViewModel.MessagePinCommand, message, fullInfo.PinnedMessageId == message.Id ? Strings.Resources.UnpinMessage : Strings.Resources.PinMessage, new FontIcon { Glyph = fullInfo.PinnedMessageId == message.Id ? Icons.Unpin : Icons.Pin });
                }
                else
                {
                    flyout.CreateFlyoutItem(MessagePin_Loaded, ViewModel.MessagePinCommand, message, Strings.Resources.PinMessage, new FontIcon { Glyph = Icons.Pin });
                }
            }

            flyout.CreateFlyoutItem(MessageForward_Loaded, ViewModel.MessageForwardCommand, message, Strings.Resources.Forward, new FontIcon { Glyph = Icons.Forward });
            flyout.CreateFlyoutItem(MessageReport_Loaded, ViewModel.MessageReportCommand, message, Strings.Resources.ReportChat, new FontIcon { Glyph = Icons.Report });
            flyout.CreateFlyoutItem(MessageDelete_Loaded, ViewModel.MessageDeleteCommand, message, Strings.Resources.Delete, new FontIcon { Glyph = Icons.Delete });
            flyout.CreateFlyoutItem(MessageSelect_Loaded, ViewModel.MessageSelectCommand, message, Strings.Additional.Select, new FontIcon { Glyph = Icons.Select });

            flyout.CreateFlyoutSeparator();

            // Copy
            flyout.CreateFlyoutItem(MessageCopy_Loaded, ViewModel.MessageCopyCommand, message, Strings.Resources.Copy, new FontIcon { Glyph = Icons.Copy });
            flyout.CreateFlyoutItem(MessageCopyLink_Loaded, ViewModel.MessageCopyLinkCommand, message, Strings.Resources.CopyLink, new FontIcon { Glyph = Icons.CopyLink });
            flyout.CreateFlyoutItem(MessageCopyMedia_Loaded, ViewModel.MessageCopyMediaCommand, message, Strings.Additional.CopyImage, new FontIcon { Glyph = Icons.CopyImage });

            flyout.CreateFlyoutSeparator();

            // Stickers
            flyout.CreateFlyoutItem(MessageAddSticker_Loaded, ViewModel.MessageAddStickerCommand, message, Strings.Resources.AddToStickers, new FontIcon { Glyph = Icons.Stickers });
            flyout.CreateFlyoutItem(MessageFaveSticker_Loaded, ViewModel.MessageFaveStickerCommand, message, Strings.Resources.AddToFavorites, new FontIcon { Glyph = Icons.Favorite });
            flyout.CreateFlyoutItem(MessageUnfaveSticker_Loaded, ViewModel.MessageUnfaveStickerCommand, message, Strings.Resources.DeleteFromFavorites, new FontIcon { Glyph = Icons.Unfavorite });

            flyout.CreateFlyoutSeparator();

            // Files
            flyout.CreateFlyoutItem(MessageSaveAnimation_Loaded, ViewModel.MessageSaveAnimationCommand, message, Strings.Resources.SaveToGIFs, new FontIcon { Glyph = Icons.Animations });
            flyout.CreateFlyoutItem(MessageSaveMedia_Loaded, ViewModel.MessageSaveMediaCommand, message, Strings.Additional.SaveAs, new FontIcon { Glyph = Icons.SaveAs });
            flyout.CreateFlyoutItem(MessageSaveMedia_Loaded, ViewModel.MessageOpenFolderCommand, message, Strings.Additional.ShowInFolder, new FontIcon { Glyph = Icons.Folder });

            // Contacts
            flyout.CreateFlyoutItem(MessageAddContact_Loaded, ViewModel.MessageAddContactCommand, message, Strings.Resources.AddContactTitle, new FontIcon { Glyph = Icons.Contact });
            //CreateFlyoutItem(ref flyout, MessageSaveDownload_Loaded, ViewModel.MessageSaveDownloadCommand, messageCommon, Strings.Resources.SaveToDownloads);

            //sender.ContextFlyout = menu;

            if (flyout.Items.Count > 0 && flyout.Items[flyout.Items.Count - 1] is MenuFlyoutSeparator)
            {
                flyout.Items.RemoveAt(flyout.Items.Count - 1);
            }

            args.ShowAt(flyout, element);
        }

        private bool MessageReply_Loaded(MessageViewModel message)
        {
            //var channel = ViewModel.With as TLChannel;
            //if (channel != null && channel.MigratedFromChatId != null)
            //{
            //    if (messageCommon.ToId is TLPeerChat)
            //    {
            //        element.Visibility = messageCommon.ToId.Id == channel.MigratedFromChatId ? Visibility.Collapsed : Visibility.Visible;
            //    }
            //}

            var chat = message.GetChat();
            if (chat != null && chat.Type is ChatTypeSupergroup supergroupType)
            {
                var supergroup = ViewModel.ProtoService.GetSupergroup(supergroupType.SupergroupId);
                if (supergroup.IsChannel)
                {
                    return supergroup.Status is ChatMemberStatusCreator || supergroup.Status is ChatMemberStatusAdministrator;
                }
                else if (supergroup.Status is ChatMemberStatusRestricted restricted)
                {
                    return restricted.CanSendMessages;
                }
            }

            return true;
        }

        private bool MessagePin_Loaded(MessageViewModel message)
        {
            var chat = message.GetChat();
            if (chat != null && chat.Type is ChatTypeSupergroup supergroupType)
            {
                var supergroup = ViewModel.ProtoService.GetSupergroup(supergroupType.SupergroupId);
                if (supergroup == null)
                {
                    return false;
                }

                if (message.IsService())
                {
                    return false;
                }

                return supergroup.Status is ChatMemberStatusCreator || (supergroup.Status is ChatMemberStatusAdministrator admin && (admin.CanPinMessages || supergroup.IsChannel && admin.CanEditMessages));
            }

            return false;
        }

        private bool MessageEdit_Loaded(MessageViewModel message)
        {
            return message.CanBeEdited;
        }

        private bool MessageDelete_Loaded(MessageViewModel message)
        {
            return message.CanBeDeletedOnlyForSelf || message.CanBeDeletedForAllUsers;
        }

        private bool MessageForward_Loaded(MessageViewModel message)
        {
            return message.CanBeForwarded;
        }

        private bool MessageReport_Loaded(MessageViewModel message)
        {
            var chat = ViewModel.Chat;
            if (chat == null || !chat.CanBeReported)
            {
                return false;
            }

            if (message.IsService())
            {
                return false;
            }

            var myId = ViewModel.CacheService.Options.MyId;
            return message.SenderUserId != myId;
        }

        private bool MessageCopy_Loaded(MessageViewModel message)
        {
            if (message.Content is MessageText text)
            {
                return !string.IsNullOrEmpty(text.Text.Text);
            }
            else if (message.Content is MessageContact)
            {
                return true;
            }

            return message.Content.HasCaption();
        }

        private bool MessageCopyMedia_Loaded(MessageViewModel message)
        {
            if (message.Ttl > 0)
            {
                return false;
            }

            if (message.Content is MessagePhoto)
            {
                return true;
            }
            else if (message.Content is MessageInvoice invoice)
            {
                return invoice.Photo != null;
            }
            else if (message.Content is MessageText text)
            {
                return text.WebPage != null && text.WebPage.IsPhoto();
            }

            return false;
        }

        private bool MessageCopyLink_Loaded(MessageViewModel message)
        {
            var chat = message.GetChat();
            if (chat != null && chat.Type is ChatTypeSupergroup supergroupType)
            {
                var supergroup = ViewModel.ProtoService.GetSupergroup(supergroupType.SupergroupId);
                return !string.IsNullOrEmpty(supergroup.Username);
            }

            return false;
        }

        private bool MessageSelect_Loaded(MessageViewModel message)
        {
            if (_myPeople || message.IsService())
            {
                return false;
            }

            return true;
        }

        private bool MessageAddSticker_Loaded(MessageViewModel message)
        {
            if (message.Content is MessageSticker sticker && sticker.Sticker.SetId != 0)
            {
                return !ViewModel.ProtoService.IsStickerSetInstalled(sticker.Sticker.SetId);
            }
            else if (message.Content is MessageText text && text.WebPage?.Sticker != null && text.WebPage.Sticker.SetId != 0)
            {
                return !ViewModel.ProtoService.IsStickerSetInstalled(text.WebPage.Sticker.SetId);
            }

            return false;
        }

        private bool MessageFaveSticker_Loaded(MessageViewModel message)
        {
            if (message.Content is MessageSticker sticker && sticker.Sticker.SetId != 0)
            {
                return !ViewModel.ProtoService.IsStickerFavorite(sticker.Sticker.StickerValue.Id);
            }

            return false;
        }

        private bool MessageUnfaveSticker_Loaded(MessageViewModel message)
        {
            if (message.Content is MessageSticker sticker && sticker.Sticker.SetId != 0)
            {
                return ViewModel.ProtoService.IsStickerFavorite(sticker.Sticker.StickerValue.Id);
            }

            return false;
        }

        private bool MessageSaveMedia_Loaded(MessageViewModel message)
        {
            if (message.Ttl > 0)
            {
                return false;
            }

            var file = message.Get().GetFileAndName(true);
            if (file.File != null && file.File.Local.IsDownloadingCompleted)
            {
                return true;
            }

            return false;
        }

        private bool MessageSaveDownload_Loaded(MessageViewModel messageCommon)
        {
            return false;
        }

        private bool MessageSaveAnimation_Loaded(MessageViewModel message)
        {
            if (message.Content is MessageText text)
            {
                return text.WebPage != null && text.WebPage.Animation != null;
            }
            else if (message.Content is MessageAnimation)
            {
                return true;
            }

            return false;
        }

        private bool MessageCallAgain_Loaded(MessageViewModel message)
        {
            if (message.Content is MessageCall)
            {
                return true;
            }

            return false;
        }

        private bool MessageAddContact_Loaded(MessageViewModel message)
        {
            if (message.Content is MessageContact contact)
            {
                var user = ViewModel.ProtoService.GetUser(contact.Contact.UserId);
                if (user == null)
                {
                    return false;
                }

                if (user.OutgoingLink is LinkStateIsContact)
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }

            return false;
        }

        #endregion

        private void Stickers_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is Sticker sticker)
            {
                Stickers_ItemClick(sticker);
            }
        }

        private void Emojis_ItemClick(string emoji)
        {
            TextField.InsertText(emoji, false, false);
        }

        public async void Stickers_ItemClick(Sticker sticker)
        {
            ViewModel.SendStickerCommand.Execute(sticker);
            ViewModel.StickerPack = null;
            TextField.SetText(null, null);
            Collapse_Click(null, new RoutedEventArgs());

            await Task.Delay(100);
            TextField.FocusMaybe(FocusState.Keyboard);
        }

        public async void Animations_ItemClick(Animation animation)
        {
            ViewModel.SendAnimationCommand.Execute(animation);
            ViewModel.StickerPack = null;
            TextField.SetText(null, null);
            Collapse_Click(null, new RoutedEventArgs());

            await Task.Delay(100);
            TextField.FocusMaybe(FocusState.Keyboard);
        }

        private void TextArea_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            _textShadowVisual.Size = e.NewSize.ToVector2();
            _rootVisual.Size = e.NewSize.ToVector2();
        }

        private void ElapsedPanel_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var point = _elapsedVisual.Offset;
            point.X = (float)-e.NewSize.Width;

            _elapsedVisual.Offset = point;
            _elapsedVisual.Size = e.NewSize.ToVector2();
        }

        private void SlidePanel_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var point = _slideVisual.Offset;
            point.X = (float)e.NewSize.Width + 36;

            _slideVisual.Opacity = 0;
            _slideVisual.Offset = point;
            _slideVisual.Size = e.NewSize.ToVector2();
        }

        private void VoiceButton_RecordingStarted(object sender, EventArgs e)
        {
            var slideWidth = (float)SlidePanel.ActualWidth;
            var elapsedWidth = (float)ElapsedPanel.ActualWidth;

            _slideVisual.Opacity = 1;

            var batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

            var messageAnimation = _compositor.CreateScalarKeyFrameAnimation();
            messageAnimation.InsertKeyFrame(0, 0);
            messageAnimation.InsertKeyFrame(1, 48);
            messageAnimation.Duration = TimeSpan.FromMilliseconds(300);

            AttachTextAreaExpression();

            var slideAnimation = _compositor.CreateScalarKeyFrameAnimation();
            slideAnimation.InsertKeyFrame(0, slideWidth + 36);
            slideAnimation.InsertKeyFrame(1, 0);
            slideAnimation.Duration = TimeSpan.FromMilliseconds(300);

            var elapsedAnimation = _compositor.CreateScalarKeyFrameAnimation();
            elapsedAnimation.InsertKeyFrame(0, -elapsedWidth);
            elapsedAnimation.InsertKeyFrame(1, 0);
            elapsedAnimation.Duration = TimeSpan.FromMilliseconds(300);

            var ellipseAnimation = _compositor.CreateVector3KeyFrameAnimation();
            ellipseAnimation.InsertKeyFrame(0, new Vector3(56f / 96f));
            ellipseAnimation.InsertKeyFrame(1, new Vector3(1));
            ellipseAnimation.Duration = TimeSpan.FromMilliseconds(200);

            _messageVisual.StartAnimation("Offset.Y", messageAnimation);
            _slideVisual.StartAnimation("Offset.X", slideAnimation);
            _elapsedVisual.StartAnimation("Offset.X", elapsedAnimation);
            _ellipseVisual.StartAnimation("Scale", ellipseAnimation);

            batch.Completed += (s, args) =>
            {
                _elapsedTimer.Start();

                AttachExpression();
                //DetachTextAreaExpression();
            };
            batch.End();

            ViewModel.ChatActionManager.SetTyping(btnVoiceMessage.IsChecked.Value ? (ChatAction)new ChatActionRecordingVideoNote() : new ChatActionRecordingVoiceNote());
        }

        private void VoiceButton_RecordingStopped(object sender, EventArgs e)
        {
            AttachExpression();
            AttachTextAreaExpression();

            var slidePosition = (float)(LayoutRoot.ActualWidth - 48 - 36);
            var difference = (float)(slidePosition - ElapsedPanel.ActualWidth);

            var batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

            var slideAnimation = _compositor.CreateScalarKeyFrameAnimation();
            slideAnimation.InsertKeyFrame(0, _slideVisual.Offset.X);
            slideAnimation.InsertKeyFrame(1, -slidePosition);
            slideAnimation.Duration = TimeSpan.FromMilliseconds(200);

            var messageAnimation = _compositor.CreateScalarKeyFrameAnimation();
            messageAnimation.InsertKeyFrame(0, 48);
            messageAnimation.InsertKeyFrame(1, 0);
            messageAnimation.Duration = TimeSpan.FromMilliseconds(200);

            _slideVisual.StartAnimation("Offset.X", slideAnimation);
            _messageVisual.StartAnimation("Offset.Y", messageAnimation);

            batch.Completed += (s, args) =>
            {
                _elapsedTimer.Stop();

                DetachExpression();
                //DetachTextAreaExpression();

                ElapsedLabel.Text = "0:00,0";

                var point = _slideVisual.Offset;
                point.X = _slideVisual.Size.X + 36;

                _slideVisual.Opacity = 0;
                _slideVisual.Offset = point;

                point = _elapsedVisual.Offset;
                point.X = -_elapsedVisual.Size.X;

                _elapsedVisual.Offset = point;
            };
            batch.End();

            ViewModel.ChatActionManager.CancelTyping();
        }

        private void VoiceButton_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e)
        {
            var cumulative = (float)e.Cumulative.Translation.X;
            var point = _slideVisual.Offset;
            point.X = Math.Min(0, cumulative);

            _slideVisual.Offset = point;

            if (point.X < -72)
            {
                e.Complete();
                btnVoiceMessage.CancelRecording();
            }
        }

        private void AttachExpression()
        {
            var elapsedExpression = _compositor.CreateExpressionAnimation("min(0, slide.Offset.X + ((root.Size.X - 48 - 36 - slide.Size.X) - elapsed.Size.X))");
            elapsedExpression.SetReferenceParameter("slide", _slideVisual);
            elapsedExpression.SetReferenceParameter("elapsed", _elapsedVisual);
            elapsedExpression.SetReferenceParameter("root", _rootVisual);

            var ellipseExpression = _compositor.CreateExpressionAnimation("Vector3(max(0, 1 + slide.Offset.X / (root.Size.X - 48 - 36)), max(0, 1 + slide.Offset.X / (root.Size.X - 48 - 36)), 1)");
            ellipseExpression.SetReferenceParameter("slide", _slideVisual);
            ellipseExpression.SetReferenceParameter("elapsed", _elapsedVisual);
            ellipseExpression.SetReferenceParameter("root", _rootVisual);

            _elapsedVisual.StopAnimation("Offset.X");
            _elapsedVisual.StartAnimation("Offset.X", elapsedExpression);

            _ellipseVisual.StopAnimation("Scale");
            _ellipseVisual.StartAnimation("Scale", ellipseExpression);
        }

        private void DetachExpression()
        {
            _elapsedVisual.StopAnimation("Offset.X");
            _ellipseVisual.StopAnimation("Scale");
        }

        private void AttachTextAreaExpression()
        {
            AttachTextAreaExpression(ButtonAttach);
            AttachTextAreaExpression(ButtonSilent);
            AttachTextAreaExpression(ButtonTimer);
            AttachTextAreaExpression(btnCommands);
            AttachTextAreaExpression(btnStickers);
            AttachTextAreaExpression(ButtonEdit);
            AttachTextAreaExpression(btnSendMessage);
        }

        private void AttachTextAreaExpression(FrameworkElement element)
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);

            var expression = _compositor.CreateExpressionAnimation("visual.Offset.Y");
            expression.SetReferenceParameter("visual", _messageVisual);

            visual.StopAnimation("Offset.Y");
            visual.StartAnimation("Offset.Y", expression);
        }

        private void DetachTextAreaExpression()
        {
            DetachTextAreaExpression(ButtonAttach);
            DetachTextAreaExpression(ButtonSilent);
            DetachTextAreaExpression(ButtonTimer);
            DetachTextAreaExpression(btnCommands);
            DetachTextAreaExpression(btnStickers);
            DetachTextAreaExpression(ButtonEdit);
            DetachTextAreaExpression(btnSendMessage);
        }

        private void DetachTextAreaExpression(FrameworkElement element)
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            visual.StopAnimation("Offset.Y");
        }

        private void Autocomplete_ItemClick(object sender, ItemClickEventArgs e)
        {
            var chat = ViewModel.Chat;
            if (chat == null)
            {
                return;
            }

            TextField.Document.GetText(TextGetOptions.None, out string hidden);
            TextField.Document.GetText(TextGetOptions.NoHidden, out string text);

            if (e.ClickedItem is User user && ChatTextBox.SearchByUsername(text.Substring(0, Math.Min(TextField.Document.Selection.EndPosition, text.Length)), out string username, out int index))
            {
                var insert = string.Empty;
                var adjust = 0;

                if (string.IsNullOrEmpty(user.Username))
                {
                    insert = string.IsNullOrEmpty(user.FirstName) ? user.LastName : user.FirstName;
                    adjust = 1;
                }
                else
                {
                    insert = user.Username;
                }

                var range = TextField.Document.GetRange(TextField.Document.Selection.StartPosition - username.Length - adjust, TextField.Document.Selection.StartPosition);
                range.SetText(TextSetOptions.None, insert);

                if (string.IsNullOrEmpty(user.Username))
                {
                    range.Link = $"\"tg-user://{user.Id}\"";
                }

                TextField.Document.GetRange(range.EndPosition, range.EndPosition).SetText(TextSetOptions.None, " ");
                TextField.Document.Selection.StartPosition = range.EndPosition + 1;

                if (index == 0 && user.Type is UserTypeBot bot && bot.IsInline)
                {
                    ViewModel.ResolveInlineBot(user.Username);
                }
            }
            else if (e.ClickedItem is UserCommand command)
            {
                var insert = $"/{command.Item.Command}";
                if (chat.Type is ChatTypeSupergroup || chat.Type is ChatTypeBasicGroup)
                {
                    var bot = ViewModel.ProtoService.GetUser(command.UserId);
                    if (bot != null && bot.Username.Length > 0)
                    {
                        insert += $"@{bot.Username}";
                    }
                }

                TextField.SetText(null, null);
                ViewModel.SendCommand.Execute(insert);
            }
            else if (e.ClickedItem is string hashtag && ChatTextBox.SearchByHashtag(text.Substring(0, Math.Min(TextField.Document.Selection.EndPosition, text.Length)), out string initial, out int index2))
            {
                var insert = $"{hashtag} ";
                var start = TextField.Document.Selection.StartPosition - 1 - initial.Length + insert.Length;
                var range = TextField.Document.GetRange(TextField.Document.Selection.StartPosition - 1 - initial.Length, TextField.Document.Selection.StartPosition);
                range.SetText(TextSetOptions.None, insert);

                //TextField.Document.GetRange(start, start).SetText(TextSetOptions.None, " ");
                //TextField.Document.Selection.StartPosition = start + 1;
                TextField.Document.Selection.StartPosition = start;
            }
            else if (e.ClickedItem is EmojiSuggestion emoji && ChatTextBox.SearchByEmoji(text.Substring(0, Math.Min(TextField.Document.Selection.EndPosition, text.Length)), out string replacement))
            {
                var insert = $"{emoji.Emoji} ";
                var start = TextField.Document.Selection.StartPosition - 1 - replacement.Length + insert.Length;
                var range = TextField.Document.GetRange(TextField.Document.Selection.StartPosition - 1 - replacement.Length, TextField.Document.Selection.StartPosition);
                range.SetText(TextSetOptions.None, insert);

                //TextField.Document.GetRange(start, start).SetText(TextSetOptions.None, " ");
                //TextField.Document.Selection.StartPosition = start + 1;
                TextField.Document.Selection.StartPosition = start;
            }

            ViewModel.Autocomplete = null;
        }

        #region Binding

        //public Visibility ConvertBotInfo(TLBotInfo info, bool last)
        //{
        //    return info != null && !string.IsNullOrEmpty(info.Description) && last ? Visibility.Visible : Visibility.Collapsed;
        //}

        private Visibility ConvertShadowVisibility(Visibility inline, object stickers, object autocomplete)
        {
            _textShadowVisual.IsVisible = inline == Visibility.Visible || stickers != null || autocomplete != null;
            return Visibility.Visible;
        }

        public Visibility ConvertIsEmpty(bool empty, bool self, bool bot, bool should)
        {
            if (should)
            {
                return empty && self ? Visibility.Visible : Visibility.Collapsed;
            }

            return empty && !self && !bot ? Visibility.Visible : Visibility.Collapsed;
        }

        public string ConvertEmptyText(int userId)
        {
            return userId != 777000 && userId != 429000 && userId != 4244000 && (userId / 1000 == 333 || userId % 1000 == 0) ? Strings.Resources.GotAQuestion : Strings.Resources.NoMessages;
        }

        public string ConvertSelectedCount(int count, bool items)
        {
            if (items)
            {
                // TODO: Send 1 Photo/Video
                return count > 0 ? string.Format(Strings.Resources.SendItems, count) : Strings.Resources.ChatGallery;
            }
            else
            {
                return count > 0 ? count > 1 ? Strings.Resources.SendAsFiles : Strings.Resources.SendAsFile : Strings.Resources.ChatDocument;
            }
        }

        #endregion

        private void Share_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as FrameworkElement;
            if (button.Tag is MessageViewModel message)
            {
                ViewModel.MessageShareCommand.Execute(message);
            }
        }

        private async void Date_Click(object sender, RoutedEventArgs e)
        {
            //var button = sender as FrameworkElement;
            //if (button.DataContext is TLMessageCommonBase message)
            //{
            //    var dialog = new Controls.Views.CalendarView();
            //    dialog.MaxDate = DateTimeOffset.Now.Date;
            //    dialog.SelectedDates.Add(BindConvert.Current.DateTime(message.Date));

            //    var confirm = await dialog.ShowQueuedAsync();
            //    if (confirm == ContentDialogResult.Primary && dialog.SelectedDates.Count > 0)
            //    {
            //        var offset = TLUtils.DateToUniversalTimeTLInt(dialog.SelectedDates.FirstOrDefault().Date);
            //        await ViewModel.LoadDateSliceAsync(offset);
            //    }
            //}
        }

        private void Expand_Click(object sender, RoutedEventArgs e)
        {
            if (HeaderOverlay.Visibility == Visibility.Visible)
            {
                StickersPanel.MinHeight = 260;
                StickersPanel.MaxHeight = 360;
                StickersPanel.Height = _lastKnownKeyboardHeight;
                ButtonExpand.Glyph = "\uE010";

                HeaderOverlay.Visibility = Visibility.Collapsed;
                UnmaskTitleAndStatusBar();
            }
            else
            {
                StickersPanel.MinHeight = ActualHeight - 48 * 2;
                StickersPanel.MaxHeight = ActualHeight - 48 * 2;
                StickersPanel.Height = double.NaN;
                ButtonExpand.Glyph = "\uE011";

                HeaderOverlay.Visibility = Visibility.Visible;
                MaskTitleAndStatusBar();
            }
        }

        private void Collapse_Click(object sender, RoutedEventArgs e)
        {
            if (ApiInfo.IsFullExperience)
            {
                if (StickersPanel.Visibility == Visibility.Collapsed || !_stickersOpen)
                {
                    return;
                }

                _stickersOpen = false;

                var batch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
                batch.Completed += (s, args) =>
                {
                    StickersPanel.Visibility = Visibility.Collapsed;
                };

                var opacity = _compositor.CreateScalarKeyFrameAnimation();
                opacity.InsertKeyFrame(0, 1);
                opacity.InsertKeyFrame(1, 0);

                var clip = _compositor.CreateScalarKeyFrameAnimation();
                clip.InsertKeyFrame(0, 0);
                clip.InsertKeyFrame(1, 48);

                _stickersPanel.StartAnimation("Opacity", opacity);
                _stickersPanel.Clip.StartAnimation("LeftInset", clip);
                _stickersPanel.Clip.StartAnimation("TopInset", clip);

                batch.End();
            }
            else
            {
                if ((HeaderOverlay.Visibility == Visibility.Visible && sender == null) || e != null)
                {
                    StickersPanel.MinHeight = 260;
                    StickersPanel.MaxHeight = 360;
                    StickersPanel.Height = _lastKnownKeyboardHeight;
                    ButtonExpand.Glyph = "\uE010";

                    HeaderOverlay.Visibility = Visibility.Collapsed;
                    UnmaskTitleAndStatusBar();
                }
                else
                {
                    _stickersOpen = false;

                    StickersPanel.MinHeight = 260;
                    StickersPanel.MaxHeight = 360;
                    StickersPanel.Height = _lastKnownKeyboardHeight;
                    ButtonExpand.Glyph = "\uE010";

                    HeaderOverlay.Visibility = Visibility.Collapsed;
                    UnmaskTitleAndStatusBar();

                    StickersPanel.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void StickersPanel_VisibilityChanged(DependencyObject sender, DependencyProperty dp)
        {
            if (StickersPanel.Visibility == Visibility.Collapsed)
            {
                HeaderOverlay.Visibility = Visibility.Collapsed;
                UnmaskTitleAndStatusBar();
            }
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (HeaderOverlay.Visibility == Visibility.Visible)
            {
                StickersPanel.MinHeight = e.NewSize.Height - 48 * 2;
                StickersPanel.MaxHeight = e.NewSize.Height - 48 * 2;
            }
        }

        private void Autocomplete_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var height = ListAutocomplete.ItemsPanelRoot.ActualHeight;
            var padding = ListAutocomplete.ActualHeight - Math.Min(154, ListAutocomplete.Items.Count * 44);

            //ListAutocomplete.Padding = new Thickness(0, padding, 0, 0);
            AutocompleteHeader.Margin = new Thickness(0, padding, 0, -height);
            AutocompleteHeader.Height = height;

            Debug.WriteLine("Autocomplete size changed");
        }

        private void MaskTitleAndStatusBar()
        {
            var titlebar = ApplicationView.GetForCurrentView().TitleBar;
            var backgroundBrush = Application.Current.Resources["TelegramTitleBarBackgroundBrush"] as SolidColorBrush;
            var foregroundBrush = Application.Current.Resources["SystemControlForegroundBaseHighBrush"] as SolidColorBrush;
            var overlayBrush = new SolidColorBrush(Color.FromArgb(0x99, 0x00, 0x00, 0x00));

            if (overlayBrush != null)
            {
                var maskBackground = ColorsHelper.AlphaBlend(backgroundBrush.Color, overlayBrush.Color);
                var maskForeground = ColorsHelper.AlphaBlend(foregroundBrush.Color, overlayBrush.Color);

                titlebar.BackgroundColor = maskBackground;
                titlebar.ForegroundColor = maskForeground;
                //titlebar.ButtonBackgroundColor = maskBackground;
                titlebar.ButtonForegroundColor = maskForeground;

                if (ApiInformation.IsTypePresent("Windows.UI.ViewManagement.StatusBar"))
                {
                    var statusBar = StatusBar.GetForCurrentView();
                    statusBar.BackgroundColor = maskBackground;
                    statusBar.ForegroundColor = maskForeground;
                }
            }
        }

        private void UnmaskTitleAndStatusBar()
        {
            TLWindowContext.GetForCurrentView().UpdateTitleBar();
        }

        private void Autocomplete_Loaded(object sender, RoutedEventArgs e)
        {
            var padding = ActualHeight - 48 * 2 - 152;

            var boh = ListAutocomplete.Descendants().FirstOrDefault();

            _autocompleteLayer = ElementCompositionPreview.GetElementVisual(ListAutocomplete);
            _autocompleteLayer.Clip = _autocompleteInset = _compositor.CreateInsetClip(0, (float)padding, 0, 0);

            var scroll = ListAutocomplete.Descendants<ScrollViewer>().FirstOrDefault() as ScrollViewer;
            if (scroll != null)
            {
                //_scrollingHost = scroll;
                //_scrollingHost.ChangeView(null, 0, null, true);
                //scroll.ViewChanged += Scroll_ViewChanged;
                //Scroll_ViewChanged(scroll, null);

                //var brush = App.Current.Resources["SystemControlBackgroundChromeMediumLowBrush"] as SolidColorBrush;
                var props = ElementCompositionPreview.GetScrollViewerManipulationPropertySet(scroll);

                //if (_backgroundVisual == null)
                //{
                //    _backgroundVisual = ElementCompositionPreview.GetElementVisual(BackgroundPanel).Compositor.CreateSpriteVisual();
                //    ElementCompositionPreview.SetElementChildVisual(BackgroundPanel, _backgroundVisual);
                //}

                //_backgroundVisual.Brush = _backgroundVisual.Compositor.CreateColorBrush(brush.Color);
                //_backgroundVisual.Size = new System.Numerics.Vector2((float)BackgroundPanel.ActualWidth, (float)BackgroundPanel.ActualHeight);
                //_backgroundVisual.Clip = _backgroundVisual.Compositor.CreateInsetClip();

                //_expression = _expression ?? _backgroundVisual.Compositor.CreateExpressionAnimation("Max(Maximum, Scrolling.Translation.Y)");
                //_expression.SetReferenceParameter("Scrolling", props);
                //_expression.SetScalarParameter("Maximum", -(float)BackgroundPanel.Margin.Top + 1);
                //_backgroundVisual.StopAnimation("Offset.Y");
                //_backgroundVisual.StartAnimation("Offset.Y", _expression);


                ExpressionAnimation _expressionClip = null;
                //_expressionClip = _expressionClip ?? _compositor.CreateExpressionAnimation("Min(0, Maximum - Scrolling.Translation.Y)");
                _expressionClip = _expressionClip ?? _compositor.CreateExpressionAnimation("Scrolling.Translation.Y");
                _expressionClip.SetReferenceParameter("Scrolling", props);
                _expressionClip.SetScalarParameter("Maximum", -(float)padding);
                _autocompleteLayer.Clip.StopAnimation("Offset.Y");
                _autocompleteLayer.Clip.StartAnimation("Offset.Y", _expressionClip);
            }

            //var panel = List.ItemsPanelRoot as ItemsWrapGrid;
            //if (panel != null)
            //{
            //    panel.SizeChanged += (s, args) =>
            //    {
            //        Scroll_ViewChanged(scroll, null);
            //    };
            //}
        }

        private void Mentions_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            ViewModel.ReadMentionsCommand.Execute();
        }

        private void ItemsStackPanel_Loading(FrameworkElement sender, object args)
        {
            Messages.SetScrollMode();
        }

        private void ServiceMessage_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as FrameworkElement;
            var message = button.Tag as MessageViewModel;

            if (message == null)
            {
                return;
            }

            ViewModel.MessageServiceCommand.Execute(message);
        }

        private void Autocomplete_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue)
            {
                return;
            }

            var content = args.ItemContainer.ContentTemplateRoot as Grid;

            if (args.Item is UserCommand userCommand)
            {
                var photo = content.Children[0] as ProfilePicture;
                var title = content.Children[1] as TextBlock;

                var command = title.Inlines[0] as Run;
                var description = title.Inlines[1] as Run;

                command.Text = $"/{userCommand.Item.Command} ";
                description.Text = userCommand.Item.Description;

                var user = ViewModel.ProtoService.GetUser(userCommand.UserId);
                if (user == null)
                {
                    return;
                }

                photo.Source = PlaceholderHelper.GetUser(ViewModel.ProtoService, user, 36);
            }
            else if (args.Item is User user)
            {
                var photo = content.Children[0] as ProfilePicture;
                var title = content.Children[1] as TextBlock;

                var name = title.Inlines[0] as Run;
                var username = title.Inlines[1] as Run;

                name.Text = user.GetFullName();
                username.Text = string.IsNullOrEmpty(user.Username) ? string.Empty : $" @{user.Username}";

                photo.Source = PlaceholderHelper.GetUser(ViewModel.ProtoService, user, 36);
            }
        }

        private async void StickerPack_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            var content = args.ItemContainer.ContentTemplateRoot as Image;

            if (args.InRecycleQueue)
            {
                content.Source = null;
                return;
            }

            var sticker = args.Item as Sticker;

            if (sticker == null || sticker.Thumbnail == null)
            {
                content.Source = null;
                return;
            }

            args.ItemContainer.Tag = content.Tag = new ViewModels.Dialogs.StickerViewModel(ViewModel.ProtoService, ViewModel.Aggregator, sticker);

            //if (args.Phase < 2)
            //{
            //    content.Source = null;
            //    args.RegisterUpdateCallback(Stickers_ContainerContentChanging);
            //}
            //else
            if (args.Phase == 0)
            {
                var file = sticker.Thumbnail.Photo;
                if (file.Local.IsDownloadingCompleted)
                {
                    content.Source = await PlaceholderHelper.GetWebpAsync(file.Local.Path);
                }
                else if (file.Local.CanBeDownloaded && !file.Local.IsDownloadingActive)
                {
                    //DownloadFile(file.Id, sticker);
                    ViewModel.ProtoService.Send(new DownloadFile(file.Id, 1, 0));
                }
            }
            else
            {
                throw new System.Exception("We should be in phase 0, but we are not.");
            }

            args.Handled = true;
        }

        private void ShowAction(string content, bool enabled)
        {
            ActionLabel.Text = content;
            Action.IsEnabled = enabled;
            Action.Visibility = Visibility.Visible;
            TextArea.Visibility = Visibility.Collapsed;

            Messages.Focus(FocusState.Programmatic);
        }

        private void ShowArea()
        {
            Action.IsEnabled = false;
            Action.Visibility = Visibility.Collapsed;
            TextArea.Visibility = Visibility.Visible;

            TextField.FocusMaybe(FocusState.Keyboard);
        }

        private bool StillValid(Chat chat)
        {
            return chat?.Id == ViewModel?.Chat?.Id;
        }

        #region UI delegate

        public void UpdateChat(Chat chat)
        {
            UpdateChatTitle(chat);
            UpdateChatPhoto(chat);

            UpdateChatUnreadMentionCount(chat, chat.UnreadMentionCount);

            Report.Visibility = chat.CanBeReported ? Visibility.Visible : Visibility.Collapsed;
            ReportSpam.Text = chat.Type is ChatTypePrivate || chat.Type is ChatTypeSecret ? Strings.Resources.ReportSpam : Strings.Resources.ReportSpamAndLeave;

            ButtonTimer.Visibility = chat.Type is ChatTypeSecret ? Visibility.Visible : Visibility.Collapsed;
            ButtonSilent.Visibility = chat.Type is ChatTypeSupergroup supergroup && supergroup.IsChannel ? Visibility.Visible : Visibility.Collapsed;
            ButtonSilent.IsChecked = chat.DefaultDisableNotification;

            Call.Visibility = Visibility.Collapsed;
            CallPlaceholder.Visibility = Visibility.Collapsed;
        }

        public void UpdateChatTitle(Chat chat)
        {
            Title.Text = ViewModel.CacheService.GetTitle(chat);
        }

        public void UpdateChatPhoto(Chat chat)
        {
            Photo.Source = PlaceholderHelper.GetChat(ViewModel.ProtoService, chat, (int)Photo.Width);
        }

        public void UpdateChatDefaultDisableNotification(Chat chat, bool defaultDisableNotification)
        {
            if (chat.Type is ChatTypeSupergroup supergroup && supergroup.IsChannel)
            {
                ButtonSilent.IsChecked = defaultDisableNotification;

                TextField.PlaceholderText = chat.DefaultDisableNotification
                    ? Strings.Resources.ChannelSilentBroadcast
                    : Strings.Resources.ChannelBroadcast;
            }
        }

        public void UpdateChatActions(Chat chat, IDictionary<int, ChatAction> actions)
        {
            if (chat.Type is ChatTypePrivate privata && privata.UserId == ViewModel.CacheService.Options.MyId)
            {
                ChatActionIndicator.UpdateAction(null);
                ChatActionPanel.Visibility = Visibility.Collapsed;
                Subtitle.Opacity = 1;
                return;
            }

            if (actions != null && actions.Count > 0)
            {
                ChatActionLabel.Text = InputChatActionManager.GetTypingString(chat, actions, ViewModel.CacheService.GetUser, out ChatAction commonAction);
                ChatActionIndicator.UpdateAction(commonAction);
                ChatActionPanel.Visibility = Visibility.Visible;
                Subtitle.Opacity = 0;
            }
            else
            {
                ChatActionIndicator.UpdateAction(null);
                ChatActionPanel.Visibility = Visibility.Collapsed;
                Subtitle.Opacity = 1;
            }
        }


        public void UpdateChatNotificationSettings(Chat chat)
        {
            if (chat.Type is ChatTypeSupergroup super && super.IsChannel)
            {
                var group = ViewModel.ProtoService.GetSupergroup(super.SupergroupId);
                if (group == null)
                {
                    return;
                }

                if (group.Status is ChatMemberStatusCreator || group.Status is ChatMemberStatusAdministrator administrator && administrator.CanPostMessages)
                {
                }
                else if (group.Status is ChatMemberStatusLeft)
                {
                }
                else
                {
                    ShowAction(chat.NotificationSettings.MuteFor == 0 ? Strings.Resources.ChannelMute : Strings.Resources.ChannelUnmute, true);
                }
            }
        }



        public void UpdateChatUnreadMentionCount(Chat chat, int count)
        {
            if (count > 0)
            {
                MentionsPanel.Visibility = Visibility.Visible;
                Mentions.Text = count.ToString();
            }
            else
            {
                MentionsPanel.Visibility = Visibility.Collapsed;
            }
        }

        public void UpdateChatReplyMarkup(Chat chat, MessageViewModel message)
        {
            var updated = ReplyMarkup.Update(message, message?.ReplyMarkup, false);
            if (updated)
            {
                ButtonMarkup.Visibility = Visibility.Visible;
                ShowMarkup();
            }
            else
            {
                ButtonMarkup.Visibility = Visibility.Collapsed;
                CollapseMarkup(false);
            }
        }

        public void UpdatePinnedMessage(Chat chat, MessageViewModel message, bool loading)
        {
            if (message == null && !loading)
            {
                PinnedMessagePanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                PinnedMessagePanel.Visibility = Visibility.Visible;
                PinnedMessage.UpdateMessage(message, loading, Strings.Resources.PinnedMessage);
            }
        }

        public void UpdateCallbackQueryAnswer(Chat chat, MessageViewModel message)
        {
            if (message == null)
            {
                CallbackQueryAnswerPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                CallbackQueryAnswerPanel.Visibility = Visibility.Visible;
                CallbackQueryAnswer.UpdateMessage(message, false, null);
            }
        }

        public void UpdateComposerHeader(Chat chat, MessageComposerHeader header)
        {
            if (header == null)
            {
                // Let's reset
                ComposerHeader.Visibility = Visibility.Collapsed;
                ComposerHeaderReference.Message = null;

                AttachMedia.Command = ViewModel.SendMediaCommand;
                AttachDocument.Command = ViewModel.SendDocumentCommand;

                var rights = ViewModel.VerifyRights(chat, x => x.CanSendMediaMessages, Strings.Resources.AttachMediaRestrictedForever, Strings.Resources.AttachMediaRestricted, out string label);

                AttachRecent.Height = rights ? 0 : double.NaN;
                AttachRestriction.Tag = label ?? string.Empty;
                AttachRestriction.Visibility = rights ? Visibility.Visible : Visibility.Collapsed;
                AttachMedia.Visibility = rights ? Visibility.Collapsed : Visibility.Visible;
                AttachDocument.Visibility = rights ? Visibility.Collapsed : Visibility.Visible;
                AttachLocation.Visibility = Visibility.Visible;
                AttachContact.Visibility = Visibility.Visible;
                AttachCurrent.Visibility = Visibility.Collapsed;

                ButtonAttach.Glyph = ReplyInfoToGlyphConverter.AttachGlyph;
                ButtonAttach.IsEnabled = true;

                ButtonsPanel.Visibility = Visibility.Visible;
                btnEdit.Visibility = Visibility.Collapsed;
            }
            else
            {
                ComposerHeader.Visibility = Visibility.Visible;
                ComposerHeaderReference.Message = header;

                TextField.Reply = header;

                var editing = header.EditingMessage;
                if (editing != null)
                {
                    switch (editing.Content)
                    {
                        case MessageAnimation animation:
                        case MessageAudio audio:
                        case MessageDocument document:
                        case MessagePhoto photo:
                        case MessageVideo video:
                            ButtonAttach.Glyph = ReplyInfoToGlyphConverter.AttachEditGlyph;
                            ButtonAttach.IsEnabled = true;
                            break;
                        //case MessageVoiceNote voiceNote:
                        //    ButtonAttach.Glyph = ReplyInfoToGlyphConverter.AttachGlyph;
                        //    ButtonAttach.IsEnabled = false;
                        //    break;
                        default:
                            ButtonAttach.Glyph = ReplyInfoToGlyphConverter.AttachGlyph;
                            ButtonAttach.IsEnabled = false;
                            break;
                    }

                    AttachMedia.Command = ViewModel.EditMediaCommand;
                    AttachDocument.Command = ViewModel.EditDocumentCommand;

                    AttachRecent.Height = 0;
                    AttachRestriction.Visibility = Visibility.Collapsed;
                    AttachMedia.Visibility = Visibility.Visible;
                    AttachDocument.Visibility = Visibility.Visible;
                    AttachLocation.Visibility = Visibility.Collapsed;
                    AttachContact.Visibility = Visibility.Collapsed;
                    AttachCurrent.Visibility = editing.Content is MessagePhoto || editing.Content is MessageVideo ? Visibility.Visible : Visibility.Collapsed;

                    ComposerHeaderGlyph.Glyph = ReplyInfoToGlyphConverter.EditGlyph;

                    ButtonsPanel.Visibility = Visibility.Collapsed;
                    btnEdit.Visibility = Visibility.Visible;
                }
                else
                {
                    AttachMedia.Command = ViewModel.SendMediaCommand;
                    AttachDocument.Command = ViewModel.SendDocumentCommand;

                    var rights = ViewModel.VerifyRights(chat, x => x.CanSendMediaMessages, Strings.Resources.AttachMediaRestrictedForever, Strings.Resources.AttachMediaRestricted, out string label);

                    AttachRecent.Height = rights ? 0 : double.NaN;
                    AttachRestriction.Tag = label ?? string.Empty;
                    AttachRestriction.Visibility = rights ? Visibility.Visible : Visibility.Collapsed;
                    AttachMedia.Visibility = rights ? Visibility.Collapsed : Visibility.Visible;
                    AttachDocument.Visibility = rights ? Visibility.Collapsed : Visibility.Visible;
                    AttachLocation.Visibility = Visibility.Visible;
                    AttachContact.Visibility = Visibility.Visible;
                    AttachCurrent.Visibility = Visibility.Collapsed;

                    ButtonAttach.Glyph = ReplyInfoToGlyphConverter.AttachGlyph;
                    ButtonAttach.IsEnabled = true;

                    if (header.WebPagePreview != null)
                    {
                        ComposerHeaderGlyph.Glyph = ReplyInfoToGlyphConverter.GlobeGlyph;
                    }
                    else if (header.ReplyToMessage != null)
                    {
                        ComposerHeaderGlyph.Glyph = ReplyInfoToGlyphConverter.ReplyGlyph;
                    }
                    else
                    {
                        ComposerHeaderGlyph.Glyph = ReplyInfoToGlyphConverter.LoadingGlyph;
                    }

                    ButtonsPanel.Visibility = Visibility.Visible;
                    btnEdit.Visibility = Visibility.Collapsed;
                }
            }
        }



        public void UpdateUser(Chat chat, User user, bool secret)
        {
            if (!secret)
            {
                ShowArea();
            }

            ViewModel.ShowPinnedMessage(chat, null);

            TextField.PlaceholderText = Strings.Resources.TypeMessage;
            UpdateUserStatus(chat, user);
        }

        public void UpdateUserFullInfo(Chat chat, User user, UserFullInfo fullInfo, bool secret, bool accessToken)
        {
            if (fullInfo.IsBlocked)
            {
                ShowAction(user.Type is UserTypeBot ? Strings.Resources.BotUnblock : Strings.Resources.Unblock, true);
            }
            else if (accessToken)
            {
                ShowAction(Strings.Resources.BotStart, true);
            }
            else if (!secret)
            {
                ShowArea();
            }

            if (fullInfo.BotInfo != null)
            {
                ViewModel.BotCommands = fullInfo.BotInfo.Commands.Select(x => new UserCommand(user.Id, x)).ToList();
                ViewModel.HasBotCommands = fullInfo.BotInfo.Commands.Count > 0;
            }

            Call.Visibility = !secret && fullInfo.CanBeCalled ? Visibility.Visible : Visibility.Collapsed;
            CallPlaceholder.Visibility = !secret && fullInfo.CanBeCalled ? Visibility.Visible : Visibility.Collapsed;
        }

        public void UpdateUserStatus(Chat chat, User user)
        {
            if (ViewModel.CacheService.IsSavedMessages(user))
            {
                ViewModel.LastSeen = null;
            }
            else
            {
                ViewModel.LastSeen = LastSeenConverter.GetLabel(user, true);
            }
        }



        public void UpdateSecretChat(Chat chat, SecretChat secretChat)
        {
            if (secretChat.State is SecretChatStateReady)
            {
                ShowArea();
            }
            else if (secretChat.State is SecretChatStatePending)
            {
                ShowAction(string.Format(Strings.Resources.AwaitingEncryption, ViewModel.ProtoService.GetTitle(chat)), false);
            }
            else if (secretChat.State is SecretChatStateClosed)
            {
                ShowAction(Strings.Resources.EncryptionRejected, false);
            }
        }



        public void UpdateBasicGroup(Chat chat, BasicGroup group)
        {
            ViewModel.ShowPinnedMessage(chat, null);

            if (group.Status is ChatMemberStatusLeft)
            {
                ShowAction(Strings.Resources.DeleteThisGroup, true);

                ViewModel.LastSeen = Strings.Resources.YouLeft;
            }
            else
            {
                ShowArea();

                TextField.PlaceholderText = Strings.Resources.TypeMessage;
                ViewModel.LastSeen = Locale.Declension("Members", group.MemberCount);
            }
        }

        public void UpdateBasicGroupFullInfo(Chat chat, BasicGroup group, BasicGroupFullInfo fullInfo)
        {
            var count = 0;
            var commands = new List<UserCommand>();

            foreach (var member in fullInfo.Members)
            {
                var user = ViewModel.ProtoService.GetUser(member.UserId);
                if (user != null && user.Type is UserTypeRegular && user.Status is UserStatusOnline)
                {
                    count++;
                }

                if (member.BotInfo != null)
                {
                    commands.AddRange(member.BotInfo.Commands.Select(x => new UserCommand(member.UserId, x)).ToList());
                }
            }

            if (count > 1)
            {
                ViewModel.LastSeen = string.Format("{0}, {1}", Locale.Declension("Members", fullInfo.Members.Count), Locale.Declension("OnlineCount", count));
            }
            else
            {
                ViewModel.LastSeen = Locale.Declension("Members", fullInfo.Members.Count);
            }

            ViewModel.BotCommands = commands;
            ViewModel.HasBotCommands = commands.Count > 0;
        }



        public async void UpdateSupergroup(Chat chat, Supergroup group)
        {
            ViewModel.ShowPinnedMessage(chat, null);

            if (group.IsChannel)
            {
                if ((group.Status is ChatMemberStatusLeft && group.Username.Length > 0) || (group.Status is ChatMemberStatusCreator creator && !creator.IsMember))
                {
                    ShowAction(Strings.Resources.ChannelJoin, true);
                }
                else if (group.Status is ChatMemberStatusCreator || group.Status is ChatMemberStatusAdministrator administrator && administrator.CanPostMessages)
                {
                    ShowArea();
                }
                else if (group.Status is ChatMemberStatusLeft || group.Status is ChatMemberStatusBanned)
                {
                    ShowAction(Strings.Resources.DeleteChat, true);
                }
                else
                {
                    ShowAction(chat.NotificationSettings.MuteFor == 0 ? Strings.Resources.ChannelMute : Strings.Resources.ChannelUnmute, true);
                }
            }
            else
            {
                if ((group.Status is ChatMemberStatusLeft && group.Username.Length > 0) || (group.Status is ChatMemberStatusCreator creator && !creator.IsMember))
                {
                    ShowAction(Strings.Resources.ChannelJoin, true);
                }
                else if (group.Status is ChatMemberStatusRestricted restrictedSend && !restrictedSend.CanSendMessages)
                {
                    if (restrictedSend.IsForever())
                    {
                        ShowAction(Strings.Resources.SendMessageRestrictedForever, false);
                    }
                    else
                    {
                        ShowAction(string.Format(Strings.Resources.SendMessageRestricted, BindConvert.Current.BannedUntil(restrictedSend.RestrictedUntilDate)), false);
                    }
                }
                else if (group.Status is ChatMemberStatusLeft || group.Status is ChatMemberStatusBanned)
                {
                    ShowAction(Strings.Resources.DeleteChat, true);
                }
                else
                {
                    ShowArea();
                }
            }

            TextField.PlaceholderText = group.IsChannel
                ? chat.DefaultDisableNotification
                ? Strings.Resources.ChannelSilentBroadcast
                : Strings.Resources.ChannelBroadcast
                : Strings.Resources.TypeMessage;
            ViewModel.LastSeen = Locale.Declension(group.IsChannel ? "Subscribers" : "Members", group.MemberCount);

            if (group.IsChannel)
            {
                return;
            }

            var response = await ViewModel.ProtoService.SendAsync(new GetSupergroupMembers(group.Id, new SupergroupMembersFilterBots(), 0, 200));
            if (response is ChatMembers members)
            {
                var commands = new List<UserCommand>();

                foreach (var member in members.Members)
                {
                    if (member.BotInfo != null)
                    {
                        commands.AddRange(member.BotInfo.Commands.Select(x => new UserCommand(member.UserId, x)).ToList());
                    }
                }

                if (StillValid(chat))
                {
                    ViewModel.BotCommands = commands;
                    ViewModel.HasBotCommands = commands.Count > 0;
                }
            }

            UpdateComposerHeader(chat, ViewModel.ComposerHeader);
        }

        public async void UpdateSupergroupFullInfo(Chat chat, Supergroup group, SupergroupFullInfo fullInfo)
        {
            ViewModel.ShowPinnedMessage(chat, fullInfo);

            if (group.IsChannel || fullInfo.MemberCount > 200)
            {
                ViewModel.LastSeen = Locale.Declension(group.IsChannel ? "Subscribers" : "Members", fullInfo.MemberCount);
            }
            else
            {
                var response = await ViewModel.ProtoService.SendAsync(new GetSupergroupMembers(group.Id, new SupergroupMembersFilterRecent(), 0, 200));
                if (response is ChatMembers members && StillValid(chat))
                {
                    var count = 0;
                    foreach (var member in members.Members)
                    {
                        var user = ViewModel.ProtoService.GetUser(member.UserId);
                        if (user != null && user.Type is UserTypeRegular && user.Status is UserStatusOnline)
                        {
                            count++;
                        }
                    }

                    if (count > 1)
                    {
                        ViewModel.LastSeen = string.Format("{0}, {1}", Locale.Declension("Members", fullInfo.MemberCount), Locale.Declension("OnlineCount", count));
                    }
                    else
                    {
                        ViewModel.LastSeen = Locale.Declension("Members", fullInfo.MemberCount);
                    }
                }
                else if (StillValid(chat))
                {
                    ViewModel.LastSeen = Locale.Declension("Members", fullInfo.MemberCount);
                }
            }
        }



        public async void UpdateFile(Telegram.Td.Api.File file)
        {
            //for (int i = 0; i < Messages.Items.Count; i++)
            //{
            //    var message = Messages.Items[i] as MessageViewModel;
            //    if (message.UpdateFile(file))
            //    {
            //        var container = Messages.ContainerFromItem(message) as ListViewItem;
            //        if (container == null)
            //        {
            //            continue;
            //        }

            //        var content = container.ContentTemplateRoot as FrameworkElement;
            //        if (content is Grid grid)
            //        {
            //            content = grid.FindName("Bubble") as FrameworkElement;
            //        }

            //        if (content is MessageBubble bubble)
            //        {
            //            bubble.UpdateFile(message, file);
            //        }
            //    }
            //}

            if (_viewModel.TryGetMessagesForFileId(file.Id, out IList<MessageViewModel> messages))
            {
                foreach (var message in messages)
                {
                    message.UpdateFile(file);

                    var container = Messages.ContainerFromItem(message) as ListViewItem;
                    if (container == null)
                    {
                        continue;
                    }

                    var content = container.ContentTemplateRoot as FrameworkElement;
                    if (content is Grid grid)
                    {
                        content = grid.FindName("Bubble") as FrameworkElement;
                    }

                    if (content is MessageBubble bubble)
                    {
                        bubble.UpdateFile(message, file);
                    }
                }

                if (file.Local.IsDownloadingCompleted && file.Remote.IsUploadingCompleted)
                {
                    messages.Clear();
                }
            }

            if (file.Local.IsDownloadingCompleted && _viewModel.TryGetMessagesForPhotoId(file.Id, out IList<MessageViewModel> photos))
            {
                foreach (var message in photos)
                {
                    var container = Messages.ContainerFromItem(message) as ListViewItem;
                    if (container == null)
                    {
                        continue;
                    }

                    var content = container.ContentTemplateRoot as FrameworkElement;
                    if (content is Grid grid)
                    {
                        var photo = grid.FindName("Photo") as ProfilePicture;
                        if (photo != null)
                        {
                            if (message.IsSaved())
                            {
                                if (message.ForwardInfo is MessageForwardedFromUser fromUser)
                                {
                                    var user = message.ProtoService.GetUser(fromUser.SenderUserId);
                                    if (user != null)
                                    {
                                        photo.Source = PlaceholderHelper.GetUser(null, user, 32);
                                    }
                                }
                                else if (message.ForwardInfo is MessageForwardedPost post)
                                {
                                    var fromChat = message.ProtoService.GetChat(post.ForwardedFromChatId);
                                    if (fromChat != null)
                                    {
                                        photo.Source = PlaceholderHelper.GetChat(null, fromChat, 32);
                                    }
                                }
                            }
                            else
                            {
                                var user = message.GetSenderUser();
                                if (user != null)
                                {
                                    photo.Source = PlaceholderHelper.GetUser(null, user, 32);
                                }
                            }
                        }
                    }
                }

                photos.Clear();
            }

            var chat = ViewModel.Chat;
            if (chat != null && chat.UpdateFile(file))
            {
                if (chat.Type is ChatTypePrivate privata && privata.UserId == ViewModel.CacheService.Options.MyId)
                {
                    Photo.Source = PlaceholderHelper.GetSavedMessages(privata.UserId, (int)Photo.Width);
                }
                else
                {
                    Photo.Source = PlaceholderHelper.GetChat(null, chat, (int)Photo.Width);
                }
            }

            InlinePanel.UpdateFile(file);
            StickersPanel.UpdateFile(file);

            foreach (var item in ListAutocomplete.Items.ToArray())
            {
                if (item is UserCommand command)
                {
                    var user = ViewModel.ProtoService.GetUser(command.UserId);
                    if (user.UpdateFile(file))
                    {
                        var container = ListAutocomplete.ContainerFromItem(command) as ListViewItem;
                        if (container == null)
                        {
                            continue;
                        }

                        var content = container.ContentTemplateRoot as Grid;
                        if (content == null)
                        {
                            continue;
                        }

                        var photo = content.Children[0] as ProfilePicture;
                        photo.Source = PlaceholderHelper.GetUser(null, user, 36);
                    }
                }
                else if (item is User user)
                {
                    if (user.UpdateFile(file))
                    {
                        var container = ListAutocomplete.ContainerFromItem(user) as ListViewItem;
                        if (container == null)
                        {
                            continue;
                        }

                        var content = container.ContentTemplateRoot as Grid;
                        if (content == null)
                        {
                            continue;
                        }

                        var photo = content.Children[0] as ProfilePicture;
                        photo.Source = PlaceholderHelper.GetUser(null, user, 36);
                    }
                }
            }

            var header = ViewModel.ComposerHeader;
            if (header?.EditingMessageFileId == file.Id)
            {
                var size = Math.Max(file.Size, file.ExpectedSize);
                ComposerHeaderUpload.Value = (double)file.Remote.UploadedSize / size;
            }

            if (!file.Local.IsDownloadingCompleted)
            {
                return;
            }

            foreach (Sticker sticker in StickerPack.Items.ToArray())
            {
                if (sticker.UpdateFile(file) && file.Id == sticker.Thumbnail?.Photo.Id)
                {
                    var container = StickerPack.ContainerFromItem(sticker) as SelectorItem;
                    if (container == null)
                    {
                        continue;
                    }

                    var photo = container.ContentTemplateRoot as Image;
                    if (photo == null)
                    {
                        continue;
                    }

                    photo.Source = await PlaceholderHelper.GetWebpAsync(file.Local.Path);
                }
            }
        }

        #endregion

        private void Messages_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
        {

        }
    }
}
