# 수락 검사 자동 결과

`build.ps1` 실행 때 자동으로 생성된다. 손으로 고치지 않는다.

생성: 2026-11-24 17:54 · 통과 250 · 실패 0

| 명세 항목 | 검사 |
|---|---|
| 01-2.1 | PASS StructureTests.ApplicationLayerDirection<br>PASS StructureTests.BoundedContextsExist<br>PASS StructureTests.DomainIsPure |
| 01-2.2 | PASS VersionTests.SemVerPrecedenceExample |
| 01-3 | PASS StructureTests.ForbiddenFeaturesAbsent |
| 01-3.2 | PASS DistributionTests.DistributionAssetsAreListedAndGameAssembliesAreExcluded |
| 01-3.7 | PASS ExchangeTests.UnsentMessagesSurviveRestart |
| 01-6 | PASS PreferenceTests.TranslationsComplete<br>PASS UiTests.RenderAllScreensToPng |
| 10-11.1 | PASS GameCheckTests.RealGameNoModsZeroProblems |
| 10-11.10 | PASS GameCheckTests.RealGameNoModsZeroProblems |
| 10-11.2(합성) | PASS InstallerTests.PrecheckBlocksMixingAndPassesWhenFixed |
| 10-11.3(근사) | PASS GameCheckTests.FullMscorlibOnlyFlagsPipes |
| 10-11.4 | PASS DistributionTests.FullCorlibUsesTheWindowsPlatformProfile |
| 10-11.4(합성) | PASS InstallerTests.PrecheckBlocksMixingAndPassesWhenFixed |
| 10-11.5 | PASS GameCheckTests.SyntheticChangesJudgedExactly |
| 10-11.6 | PASS GameCheckTests.AlternateNameRecorded |
| 10-11.7 | PASS GameCheckTests.BrokenSendAreaOnly<br>PASS GameLinkDomainTests.SendBrokenLocksOnlyInput<br>PASS UiTests.SendAreaBrokenLocksOnlyInput |
| 10-11.8 | PASS GameCheckTests.CachedCheckKeepsMixingProblems<br>PASS GameCheckTests.SameFingerprintSkipsRecheck |
| 10-11.9 | PASS GameCheckTests.PluginUsesOnlyContractNames |
| 10-5.2 | PASS GameCheckTests.PluginReferencesExistInGameLibraries<br>PASS ModParkingTests.KeptSetupProblemsPointToTheButton |
| 10-7 | PASS GameCheckTests.BrokenSendAreaOnly<br>PASS GameCheckTests.CachedCheckKeepsMixingProblems<br>PASS GameLinkDomainTests.NoticeShownOnce |
| 20-2 | PASS SettingsWindowTests.UpdateSourceIsFixedWithTestOverride |
| 20-4.2 | PASS UpdateTests.NormalUpdateFlow |
| 20-4.9 | PASS UpdateTests.DamagedVersionStateKeptAndReported<br>PASS UpdateTests.LauncherKeepsRunningWhenTargetCannotStart |
| 20-5 | PASS UpdateTests.KeyFileNeedsPassword |
| 20-9.1 | PASS UpdateTests.NormalUpdateFlow |
| 20-9.10 | PASS UpdatePolicyTests.RestartDeferred |
| 20-9.11 | PASS UpdateTests.GamePayloadNoticeOnFirstRun |
| 20-9.12 | PASS UpdateTests.ReleaseStopsBeforeUpload |
| 20-9.14 | PASS UpdatePolicyTests.Decisions<br>PASS UpdateTests.NonSemVerInfoRejectedWhole<br>PASS VersionTests.SemVerPrecedenceExample |
| 20-9.15 | PASS UpdateTests.ReleaseStopsBeforeUpload |
| 20-9.2 | PASS UpdateTests.OneByteTamperedInfoRejected |
| 20-9.3 | PASS UpdateTests.TamperedExeDeleted |
| 20-9.4 | PASS UpdatePolicyTests.Decisions<br>PASS UpdateTests.LowerSequenceIgnoredAndRemembered |
| 20-9.5 | PASS UpdatePolicyTests.Decisions |
| 20-9.6 | PASS UpdatePolicyTests.RedirectRules<br>PASS UpdateTests.RedirectRules |
| 20-9.7 | PASS UpdateTests.SizeCaps |
| 20-9.8 | PASS UpdateTests.FailedStartRollsBackAndIsNotRetried |
| 20-9.9 | PASS UpdateTests.NextKeyAccepted |
| 30-3 | PASS SettingsWindowTests.HeaderIconsTooltipsAndStatusLine<br>PASS StyleReferenceTests.ApprovedPaletteAndHeaderStatesMatch<br>PASS StyleReferenceTests.HeaderPaintSurvivesRepeatedStyleAndFontSizeChanges<br>PASS StyleReferenceTests.NoticeUsesQuietThemeSurfaceAndTypographyFitsEverySize |
| 30-4.2 | PASS ScrollBarTests.ParticipantPillReadsAsAButton |
| 30-5 | PASS ScrollBarTests.NoFocusFrameAfterAClick<br>PASS ScrollBarTests.SearchWindowsFollowTheTheme<br>PASS ScrollBarTests.TooltipStepsAsideAfterAClickAndNoSeam<br>PASS SettingsWindowTests.HeaderIconsTooltipsAndStatusLine |
| 30-7 | PASS StyleReferenceTests.BubbleWidthMatchesRenderedGlyphsWithoutHiddenRightPadding<br>PASS StyleReferenceTests.OwnBubblesKeepTheirBaselineWithTimeAndAvatar |
| 30-7.2.1 | PASS UiTests.AllCombinationsLayoutAndContrast<br>PASS UiTests.RenderAllScreensToPng |
| 30-7.2.2 | PASS UiTests.PresetAndShapeIndependent |
| 30-7.2.3 | PASS DistributionTests.DistributionAssetsAreListedAndGameAssembliesAreExcluded |
| 30-7.2.4 | PASS UiTests.PresetAndShapeIndependent |
| 30-8.1 | PASS UiTests.LayoutSwitchKeepsState |
| 30-8.2 | PASS PreferenceTests.LayoutHysteresis<br>PASS UiTests.AutoLayoutByWidth |
| 30-8.3 | PASS UiTests.SidebarCollapseAndWidthSaved |
| 30-8.4 | PASS UiTests.ArchivedRoomIsReadOnly |
| 30-8.5 | PASS UiTests.AutomationAreaHiddenWhenOff |
| 30-8.6 | PASS UiTests.FaceStackMaxThree |
| 30-8.7 | PASS UiTests.AllCombinationsLayoutAndContrast<br>PASS UiTests.RenderAllScreensToPng |
| 30-8.8 | PASS UiTests.WindowSizes |
| 40-1.1 | PASS DistributionTests.FullCorlibUsesTheWindowsPlatformProfile<br>PASS DistributionTests.UnstrippedLibrariesAndLgplSourcesShipped<br>PASS GameCheckTests.PluginReferencesExistInGameLibraries<br>PASS GameLinkDomainTests.GameLogMarkers<br>PASS InstallerTests.PrecheckBlocksMixingAndPassesWhenFixed<br>PASS ModParkingTests.EarlierSetupMovesOutInstallsFreshAndComesBack |
| 40-1.2 | PASS OfflineRecordTests.ComponentTrimDoesNotDuplicate<br>PASS OfflineRecordTests.MessagesWrittenWhileAppClosedAppearOnce<br>PASS OfflineRecordTests.RetentionCleansExchangeAndRecordsForeverKeeps |
| 40-2 | PASS LobbyTests.LobbyCodeDisplay |
| 40-3 | PASS NativeSpeechContractTests.NativeSpeechContractUpdatesTheLocalBubbleAndKeepsTheFilterGate |
| 40-4 | PASS ComponentIoTests.PermitReaderAllowsTheAtomicWritersExistingHandle<br>PASS ComponentIoTests.PermitSnapshotsStayCompleteDuringAtomicReplacement<br>PASS ComponentIoTests.SnapshotReaderRejectsOversizedControlFiles |
| 40-5.3 | PASS LocatorTests.RealPcSearchFindsInstalledGame |
| 40-5.5.1 | PASS LocatorTests.OneLibraryFoundAndSaved |
| 40-5.5.2 | PASS LocatorTests.SecondLibrary |
| 40-5.5.3 | PASS LocatorTests.MovedGameFoundOnFileOperation |
| 40-5.5.4 | PASS LocatorTests.RunningFromElsewhere |
| 40-5.5.5 | PASS LocatorTests.NothingFoundKeepsSavedValue |
| 40-5.5.6 | PASS LocatorTests.StartDoesNotTouchFiles |
| 40-5.5.7 | PASS LocatorTests.SeveralCandidatesListed |
| 40-6 | PASS InstallerTests.DriftDetectedAfterGameUpdate<br>PASS InstallerTests.ExistingBepInExIsKept<br>PASS InstallerTests.FailureMidInstallRollsBack<br>PASS InstallerTests.ForeignFileAtOurPathNotOverwritten<br>PASS InstallerTests.LoaderMissingIsReported<br>PASS InstallerTests.UnreadableRecordStopsOperations<br>PASS ModParkingTests.EarlierSetupMovesOutInstallsFreshAndComesBack<br>PASS ModParkingTests.RestoreNeverOverwritesAndChecksHashes<br>PASS PanelAndLinkTests.ReinstallClosesInstallsAndRelaunches |
| 40-7 | PASS ComponentErrorNoticeTests.HealthyStatusRemovesOldWarningAndLetsARecurrenceShowAgain<br>PASS ComponentIoTests.RecoveredOperationClearsOnlyItsOwnCurrentFault |
| 40-7.1 | PASS InstallerTests.InstallWritesFilesAndRecord |
| 40-7.10 | PASS ExchangeTests.ForgedRequestsRefused |
| 40-7.2 | PASS InstallerTests.ReinstallSameVersionChangesNothing |
| 40-7.3 | PASS InstallerTests.NewVersionAddsOnlyMissingFiles |
| 40-7.4 | PASS InstallerTests.ChangedOwnFileIsNotOverwritten |
| 40-7.5 | PASS InstallerTests.OtherLoaderOrPluginStopsInstall |
| 40-7.6 | PASS InstallerTests.GameRunningAsksForRestart |
| 40-7.7 | PASS InstallerTests.DisableEnableRestoresHashes |
| 40-7.8 | PASS InstallerTests.UninstallRestoresOriginalFolder |
| 40-7.9 | PASS ExchangeTests.PartialLastLineIsNotRead<br>PASS ExchangeTests.SchemaMajorAndMinorRules<br>PASS ExchangeTests.StaleStatusShowsDisconnected<br>PASS GameLinkDomainTests.ExchangeCompatibilityRules |
| 50-1 | PASS ConversationTests.ContinuationAndDateSeparators<br>PASS StyleReferenceTests.BubbleWidthMatchesRenderedGlyphsWithoutHiddenRightPadding<br>PASS StyleReferenceTests.MultilineDraftRoundTripsWithoutTreatingMarkupAsFormatting |
| 50-1.선택 | PASS WindowTests.PartialSelectionCopyKeepAndRelease |
| 50-2.그림 | PASS FilterAndPictureAppTests.EmotePictureOrNameShown<br>PASS PictureQueueTests.OncePerKeyOnePerFrameAndThreeFailures |
| 50-3.묶기 | PASS OfflineRecordTests.TextOrEmoteBetweenMeowsStartsNewGroup |
| 50-4 | PASS StorageTests.TailReadAndRooms |
| 50-5 | PASS UiTests.SearchJumpsToUnpaintedRow |
| 50-7 | PASS ExchangeTests.SendFlowEndToEnd<br>PASS ExchangeTests.UnconfirmedAfterTenSeconds<br>PASS ExchangeTests.UnsentMessagesSurviveRestart<br>PASS NativeSpeechContractTests.NativeSpeechContractUpdatesTheLocalBubbleAndKeepsTheFilterGate<br>PASS SendingTests.SendChecks<br>PASS SendingTests.SendLogCappedAt500<br>PASS SendingTests.StatesAndTimeout<br>PASS StyleReferenceTests.MultilineDraftRoundTripsWithoutTreatingMarkupAsFormatting<br>PASS StyleReferenceTests.SendingStillRoundTripsAfterHeaderAndFontChanges |
| 50-7.짝짓기 | PASS SendingTests.StrictPairing |
| 50-7.필터 | PASS FilterAndPictureAppTests.FilteredMessageShownAsNotDelivered<br>PASS StyleReferenceTests.RejectedFilterNeverInvokesTransmission |
| 50-9.1 | PASS ConversationTests.BodyIsKeptVerbatim<br>PASS ExchangeTests.HistoryWriteFailureLosesNothing |
| 50-9.10 | PASS ConversationTests.ExportIncludesHiddenMeowSummary |
| 50-9.2 | PASS ConversationTests.EmoteWindowBoundariesPerMode<br>PASS ConversationTests.ModesBAndC<br>PASS ConversationTests.SevenSameEmotesGroupToOneLine<br>PASS ConversationTests.TextOrMeowInBetweenSplitsGroup |
| 50-9.3 | PASS ConversationTests.MeowExactMatchOnly<br>PASS ConversationTests.MeowGapBoundary<br>PASS ConversationTests.MeowListMatchesThePhraseFixture<br>PASS ConversationTests.MeowPeopleCapAndTooltip<br>PASS ConversationTests.MeowsDoNotPushOutNormalMessages<br>PASS OfflineRecordTests.TextOrEmoteBetweenMeowsStartsNewGroup |
| 50-9.4 | PASS ConversationTests.HistoryWindowSteps |
| 50-9.5 | PASS StorageTests.ThreeThousandLoadTime<br>PASS UiTests.FirstDisplayOf3000 |
| 50-9.6 | PASS ConversationTests.SearchCyclesAndFindsAliases<br>PASS UiTests.SearchJumpsToUnpaintedRow |
| 50-9.7 | PASS StyleReferenceTests.SendingStillRoundTripsAfterHeaderAndFontChanges<br>PASS UiTests.ImeEnterDoesNotSend |
| 50-9.8 | PASS ExchangeTests.SendFlowEndToEnd<br>PASS SendingTests.RejectRetryKeepsDraft |
| 50-9.9 | PASS UiTests.ScrolledUpStaysPut |
| 60-1 | PASS ScrollBarTests.InitialsAreCentredInTheirCircle<br>PASS ScrollBarTests.ParticipantPillReadsAsAButton<br>PASS UiTests.FaceStackMaxThree |
| 60-4.8 | PASS StructureTests.ForbiddenFeaturesAbsent |
| 60-5.1 | PASS ConversationTests.SearchCyclesAndFindsAliases<br>PASS PeopleTests.DisplayNameModes |
| 60-5.2 | PASS PeopleTests.AliasKeyedBySteamId |
| 60-5.3 | PASS PeopleTests.AutoColorsReadableInAllThemes<br>PASS UiTests.AllCombinationsLayoutAndContrast |
| 60-5.4 | PASS UiTests.AllCombinationsLayoutAndContrast |
| 60-5.5 | PASS PeopleTests.ColorNoneDisablesPlacementAndMineKeepsTheme<br>PASS UiTests.DependentSettingsDisabled |
| 60-5.7 | PASS ExchangeTests.UnreadableParticipants<br>PASS LobbyTests.UnreadableParticipantsNotGuessed |
| 60-5.8 | PASS StorageTests.SaveFailureKeepsChangeAndWarns |
| 70-1 | PASS SettingsWindowTests.NotificationPreviewLeavesUnreadAlone |
| 70-2 | PASS ScrollBarTests.InitialsAreCentredInTheirCircle |
| 70-2.2 | PASS AppearanceTests.ReplacedPortraitGrace |
| 70-2.2.2 | PASS PictureQueueTests.OncePerKeyOnePerFrameAndThreeFailures |
| 70-2.3 | PASS StyleReferenceTests.OwnBubblesKeepTheirBaselineWithTimeAndAvatar |
| 70-3.1 | PASS ConversationTests.NotificationRules |
| 70-3.2 | PASS ConversationTests.NotificationRules |
| 70-3.3 | PASS ConversationTests.NotificationRules<br>PASS ExchangeTests.InitialHistoryDoesNotNotify |
| 70-3.4 | PASS AppearanceTests.PortraitCachePolicy |
| 70-3.5 | PASS AppearanceTests.PortraitCachePolicy |
| 70-3.6 | PASS UiTests.AllCombinationsLayoutAndContrast<br>PASS UiTests.DependentSettingsDisabled |
| 70-3.7 | PASS DistributionTests.DistributionAssetsAreListedAndGameAssembliesAreExcluded |
| 80-0 | PASS PanelAndLinkTests.AutomationNeedsNoPopUpAndKeepsTheNotice |
| 80-1.2.1 | PASS AutomationRuntimeTests.OwnChestsReceiveFreshPermitsOutsideALobby<br>PASS ChestAutomatonTests.OpensOnlyChosenReadyAffordableAfterDelay<br>PASS FilterAndPictureAppTests.PermitNeedsSwitchConsentAndConnection |
| 80-1.2.2 | PASS ChestAutomatonTests.OpensOnlyChosenReadyAffordableAfterDelay |
| 80-1.2.3 | PASS ChestAutomatonTests.OpensOnlyChosenReadyAffordableAfterDelay |
| 80-1.2.4 | PASS ChestAutomatonTests.OneAtATimeAndOncePerReadyState |
| 80-1.2.5 | PASS ChestAutomatonTests.OneAtATimeAndOncePerReadyState |
| 80-1.2.6 | PASS ChestAutomatonTests.InsufficientPointsAreExplainedWhenGameAlsoDeniesCanGet<br>PASS ChestAutomatonTests.PausesForPointsAndHourlyCap |
| 80-1.2.7 | PASS AutomationRuntimeTests.PermitChangesRebindAndRevokeWithoutTheTwoSecondDelay<br>PASS ChestAutomatonTests.StopsWithoutFreshPermitOrForOtherSession |
| 80-2 | PASS AutomationRuntimeTests.GiftPermitsRequireALobbyAndRevokeImmediatelyWhenLeaving |
| 80-2.1 | PASS GiftTests.OpensOnlyOtherPresentMembersWithPermit |
| 80-2.2 | PASS GiftTests.OpensOnlyOtherPresentMembersWithPermit |
| 80-2.3 | PASS GiftTests.WaitsForPointsAndForOwnChestOrExchange |
| 80-2.4 | PASS GiftTests.OncePerReadyStateAndSharedHourlyCap |
| 80-2.5 | PASS GiftTests.RoomChangeStopsOldPermit |
| 80-2.6 | PASS GiftTests.OncePerReadyStateAndSharedHourlyCap |
| 80-2.동의 | PASS GiftTests.SeparateConsentAndOnlyTheClickPath |
| 80-4.1 | PASS AutomationRuntimeTests.ConsentConnectionAndSupportedGameStateRemainRequired<br>PASS AutomationRuntimeTests.OwnChestsReceiveFreshPermitsOutsideALobby<br>PASS AutomationTests.ChestRules<br>PASS FilterAndPictureAppTests.PermitNeedsSwitchConsentAndConnection |
| 80-4.2 | PASS AutomationRuntimeTests.ConsentConnectionAndSupportedGameStateRemainRequired<br>PASS AutomationRuntimeTests.GiftPermitsRequireALobbyAndRevokeImmediatelyWhenLeaving<br>PASS AutomationRuntimeTests.PermitChangesRebindAndRevokeWithoutTheTwoSecondDelay<br>PASS AutomationTests.PermitExpires<br>PASS ChestAutomatonTests.StopsWithoutFreshPermitOrForOtherSession<br>PASS ComponentIoTests.PermitReaderAllowsTheAtomicWritersExistingHandle<br>PASS ComponentIoTests.PermitSnapshotsStayCompleteDuringAtomicReplacement<br>PASS ComponentIoTests.RecoveredOperationClearsOnlyItsOwnCurrentFault |
| 80-4.3 | PASS AutomationTests.ChestRules<br>PASS ChestAutomatonTests.OpensOnlyChosenReadyAffordableAfterDelay |
| 80-4.4 | PASS AutomationTests.ChestRules<br>PASS ChestAutomatonTests.PausesForPointsAndHourlyCap |
| 80-4.5 | PASS AutomationTests.ChestRules |
| 80-4.6 | PASS ChestAutomatonTests.PluginOpensChestsOnlyThroughShopClick<br>PASS GiftTests.SeparateConsentAndOnlyTheClickPath |
| 90-1 | PASS DistributionTests.BundledFontsLoadAndMeasure<br>PASS DpiStartupTests.DisplayConfigurationWriteFailureStopsBeforeUiStartup<br>PASS DpiStartupTests.MissingOrBrokenDisplayConfigurationIsRepairedWithoutTouchingPreferences<br>PASS DpiStartupTests.UiDomainUsesItsOwnPerMonitorConfiguration<br>PASS PeopleTests.ThemesTextContrast<br>PASS SettingsWindowTests.RemovedItemsStayRemoved<br>PASS StyleReferenceTests.ApprovedPaletteAndHeaderStatesMatch<br>PASS StyleReferenceTests.BubbleWidthMatchesRenderedGlyphsWithoutHiddenRightPadding<br>PASS StyleReferenceTests.DemoClearlyIdentifiesThatItDoesNotSendToTheGame<br>PASS StyleReferenceTests.HeaderPaintSurvivesRepeatedStyleAndFontSizeChanges<br>PASS StyleReferenceTests.NoticeUsesQuietThemeSurfaceAndTypographyFitsEverySize<br>PASS StyleReferenceTests.OwnBubblesKeepTheirBaselineWithTimeAndAvatar<br>PASS StyleReferenceTests.PeachOwnBubbleUsesSoftSurfaceAndSendUsesFaceColour<br>PASS StyleReferenceTests.SendingStillRoundTripsAfterHeaderAndFontChanges |
| 90-1.1 | PASS WindowTests.BorderLevelsThemesAndFocus |
| 90-1.2 | PASS WindowTests.OpacityRangeStepPreviewAndHover |
| 90-1.3 | PASS WindowTests.TitleBarFollowsThemeOnWindows11 |
| 90-1.4 | PASS ScrollBarTests.PeoplePageShowsMyNicknameAndWholePreview<br>PASS ScrollBarTests.TooltipStepsAsideAfterAClickAndNoSeam<br>PASS SettingsWindowTests.DisabledNotHiddenAndKeyboard<br>PASS SettingsWindowTests.NotificationPreviewLeavesUnreadAlone<br>PASS SettingsWindowTests.SaveFailureAndBrokenFileBanner |
| 90-1.4.1 | PASS SettingsWindowTests.SettingsWindowFollowsThemeWithoutWindowsGrey |
| 90-1.4.2 | PASS SettingsWindowTests.LabelsWrapAndNothingScrollsSideways |
| 90-1.4.3 | PASS SettingsWindowTests.NoTwoValueDropDowns |
| 90-1.4.4 | PASS SettingsWindowTests.EverySettingHasATooltipInThreeLanguagesAndARow |
| 90-1.4.5 | PASS SettingsWindowTests.RemovedItemsStayRemoved |
| 90-1.5 | PASS PeopleTests.WindowSizeSteps<br>PASS SettingsWindowTests.WindowSizeStepsAndCustom<br>PASS StyleReferenceTests.HeaderPaintSurvivesRepeatedStyleAndFontSizeChanges |
| 90-1.6 | PASS PeopleTests.ThemesTextContrast<br>PASS ScrollBarTests.SearchWindowsFollowTheTheme<br>PASS SettingsWindowTests.DarkModeAppliesEverywhereAtOnce<br>PASS SettingsWindowTests.SettingsWindowFollowsThemeWithoutWindowsGrey |
| 90-1.6.스크롤 | PASS ScrollBarTests.DrawnScrollBarsKeepWheelDragClickAndPageKeys<br>PASS ScrollBarTests.ScrollBarsFollowTheTheme |
| 90-3 | PASS SettingsWindowTests.LanguageDefaultsToKorean<br>PASS StorageTests.TailReadAndRooms |
| 90-4 | PASS UiTests.EverySettingHasLabels |
| 90-7.1 | PASS PreferenceTests.SettingsValidation<br>PASS StorageTests.SettingsPersistAcrossRestart |
| 90-7.2 | PASS ExchangeTests.HistoryWriteFailureLosesNothing<br>PASS SettingsWindowTests.SaveFailureAndBrokenFileBanner<br>PASS StorageTests.SaveFailureKeepsChangeAndWarns |
| 90-7.3 | PASS StorageTests.BrokenSettingsFile |
| 90-7.4 | PASS OfflineRecordTests.RetentionCleansExchangeAndRecordsForeverKeeps<br>PASS PreferenceTests.Retention<br>PASS StorageTests.RetentionCleanup |
| 90-7.5 | PASS PreferenceTests.DiagnosticsMasking |
| 90-7.6 | PASS PreferenceTests.TranslationsComplete<br>PASS UiTests.LanguageSwitchIsImmediate |
| 90-7.7 | PASS StorageTests.ThreeThousandLoadTime<br>PASS UiTests.FirstDisplayOf3000 |
| 95-0 | PASS ModParkingTests.ParkButtonAsksInTheWindow |
| 95-1.1 | PASS StartupTests.OnlyOneAppAndTheSecondBringsTheFirstForward |
| 95-1.2 | PASS StartupTests.FixedPathOpensTheNewestVersion |
| 95-2.1 | PASS StartupTests.StartWithGameRegistersAndCleansUp<br>PASS StartupTests.WatcherOpensOnGameStartOnly |
| 95-2.10 | PASS GroupDTests.FourFontsIncludingMalgunWindowsDefaultHaveNoClipping |
| 95-2.2 | PASS PanelAndLinkTests.UpdateCheckTimingNoticeAndDeferral |
| 95-2.3 | PASS HealthLogResendTests.UnconfirmedResendNeedsASecondPressRejectedDoesNot |
| 95-2.4 | PASS HealthLogResendTests.HealthWarnsOnlyWhenTheAppMissesWhatTheComponentGot |
| 95-2.5 | PASS HealthLogResendTests.LogFileRotatesAndKeepsNoPrivateData |
| 95-2.6 | PASS ComponentErrorNoticeTests.HealthyStatusRemovesOldWarningAndLetsARecurrenceShowAgain<br>PASS PanelAndLinkTests.ConnectionProblemsShowOneActionOnTheNoticeLine<br>PASS PanelAndLinkTests.NoticeLinePriorityAndDismissal<br>PASS StyleReferenceTests.NoticeUsesQuietThemeSurfaceAndTypographyFitsEverySize |
| 95-2.7 | PASS GroupDTests.SavedRoomsGroupUngroupSearchExportAndRetention |
| 95-2.8 | PASS GroupDTests.ParticipantsShowLastListInGreyWhenDisconnectedAndLiveWhenReconnected |
| 95-2.9 | PASS GroupDTests.FirstRunAsksLanguageInWindowWithKoreanPreselectedAndOnceOnly |
| DPI | PASS DpiStartupTests.DisplayConfigurationWriteFailureStopsBeforeUiStartup<br>PASS DpiStartupTests.MissingOrBrokenDisplayConfigurationIsRepairedWithoutTouchingPreferences<br>PASS DpiStartupTests.UiDomainUsesItsOwnPerMonitorConfiguration<br>PASS ParticipantsLayoutTests.ParticipantLabelsWrapInEveryLanguageAndFontSize<br>PASS ParticipantsLayoutTests.ShortParticipantOverlayKeepsAllTextReachable<br>PASS SettingsPreviewTests.AllPreviewRowsRemainVisibleAtLargeFontAndNarrowSettingsWidth<br>PASS SidebarLayoutTests.DefaultSidebarRemainsExpandedAfterApplyingTheWindowPreset<br>PASS SidebarLayoutTests.ShortOverlayScrollsInsteadOfCrushingItsContents<br>PASS SidebarLayoutTests.SidebarSectionsAndHeaderSurviveFontChanges<br>PASS StyleReferenceTests.DemoClearlyIdentifiesThatItDoesNotSendToTheGame |
| LGPL | PASS DistributionTests.UnstrippedLibrariesAndLgplSourcesShipped |
| automation | PASS AutomationRuntimeTests.ConsentConnectionAndSupportedGameStateRemainRequired<br>PASS AutomationRuntimeTests.GiftPermitsRequireALobbyAndRevokeImmediatelyWhenLeaving<br>PASS AutomationRuntimeTests.OwnChestsReceiveFreshPermitsOutsideALobby<br>PASS AutomationRuntimeTests.PermitChangesRebindAndRevokeWithoutTheTwoSecondDelay<br>PASS ChestAutomatonTests.InsufficientPointsAreExplainedWhenGameAlsoDeniesCanGet |
| catalog | PASS CatalogCompatibilityTests.AlreadyCompatibleCatalogAndUnexpectedPatchesRemainUntouched<br>PASS CatalogCompatibilityTests.InspectionReadsOnlyTheHeaderAndHandlesShortReads<br>PASS CatalogCompatibilityTests.KnownRuntimeMismatchChangesOnlyTheCatalogMagic<br>PASS CatalogCompatibilityTests.OnlyTheExactKnownMutableCatalogFieldIsEligible<br>PASS CatalogCompatibilityTests.ProductionPathNeverChangesFilesAndSurfacesReadErrors<br>PASS CatalogCompatibilityTests.UnknownHeadersVersionsAndShortInputsFailClosed |
| compatibility | PASS CatalogCompatibilityTests.AlreadyCompatibleCatalogAndUnexpectedPatchesRemainUntouched<br>PASS CatalogCompatibilityTests.InspectionReadsOnlyTheHeaderAndHandlesShortReads<br>PASS CatalogCompatibilityTests.KnownRuntimeMismatchChangesOnlyTheCatalogMagic<br>PASS CatalogCompatibilityTests.OnlyTheExactKnownMutableCatalogFieldIsEligible<br>PASS CatalogCompatibilityTests.ProductionPathNeverChangesFilesAndSurfacesReadErrors<br>PASS CatalogCompatibilityTests.UnknownHeadersVersionsAndShortInputsFailClosed |
| component-io | PASS ComponentErrorNoticeTests.HealthyStatusRemovesOldWarningAndLetsARecurrenceShowAgain<br>PASS ComponentIoTests.PermitReaderAllowsTheAtomicWritersExistingHandle<br>PASS ComponentIoTests.PermitSnapshotsStayCompleteDuringAtomicReplacement<br>PASS ComponentIoTests.RecoveredOperationClearsOnlyItsOwnCurrentFault<br>PASS ComponentIoTests.SnapshotReaderRejectsOversizedControlFiles |
| draft | PASS ComposeWorkflowTests.SubmitClearsImmediatelyAndRestoringNeverSilentlyReplacesANewerDraft |
| filter | PASS ComposeWorkflowTests.FilterGuidanceAndRestoreActionsFitAllSupportedLanguagesAndSizes |
| layout | PASS ComposeWorkflowTests.FilterGuidanceAndRestoreActionsFitAllSupportedLanguagesAndSizes |
| notify | PASS SettingsPreviewTests.NotificationPreviewSurvivesNormalPollingAndRestoresUnreadState |
| opacity | PASS SettingsPreviewTests.OpacityDragPreviewSurvivesHoverTimerAndCancelsWhenSettingsClose |
| participants | PASS ParticipantsLayoutTests.ParticipantLabelsWrapInEveryLanguageAndFontSize<br>PASS ParticipantsLayoutTests.ShortParticipantOverlayKeepsAllTextReachable |
| placement | PASS ComposeWorkflowTests.SettingsPreferAdjacentSpaceAndStayInsideTheOwnersWorkArea |
| portrait | PASS SettingsPreviewTests.FaceScopeUsesCompanionImageAndFallsBackForOlderConnectors<br>PASS SettingsPreviewTests.OlderConnectorExplainsFaceScopeWithoutBreakingChat<br>PASS SettingsPreviewTests.PortraitVariantsShareTheSingleFrameQueueAndRepairMissingFiles |
| preview | PASS SettingsPreviewTests.AllPreviewRowsRemainVisibleAtLargeFontAndNarrowSettingsWidth<br>PASS SettingsPreviewTests.FaceScopeUsesCompanionImageAndFallsBackForOlderConnectors<br>PASS SettingsPreviewTests.NotificationPreviewSurvivesNormalPollingAndRestoresUnreadState<br>PASS SettingsPreviewTests.OlderConnectorExplainsFaceScopeWithoutBreakingChat<br>PASS SettingsPreviewTests.OpacityDragPreviewSurvivesHoverTimerAndCancelsWhenSettingsClose<br>PASS SettingsPreviewTests.PortraitVariantsShareTheSingleFrameQueueAndRepairMissingFiles<br>PASS SettingsPreviewTests.PreviewImagesAndHeaderChangeThroughTheirActualEditors |
| restore | PASS ComposeWorkflowTests.SubmitClearsImmediatelyAndRestoringNeverSilentlyReplacesANewerDraft |
| send | PASS ComposeWorkflowTests.FilterGuidanceAndRestoreActionsFitAllSupportedLanguagesAndSizes<br>PASS ComposeWorkflowTests.SubmitClearsImmediatelyAndRestoringNeverSilentlyReplacesANewerDraft<br>PASS NativeSpeechContractTests.NativeSpeechContractUpdatesTheLocalBubbleAndKeepsTheFilterGate |
| settings | PASS ComposeWorkflowTests.SettingsPreferAdjacentSpaceAndStayInsideTheOwnersWorkArea<br>PASS ComposeWorkflowTests.ShortcutOptionsRequireSelectionAndOnlyCreateTheChosenLocation<br>PASS SettingsPreviewTests.AllPreviewRowsRemainVisibleAtLargeFontAndNarrowSettingsWidth<br>PASS SettingsPreviewTests.FaceScopeUsesCompanionImageAndFallsBackForOlderConnectors<br>PASS SettingsPreviewTests.NotificationPreviewSurvivesNormalPollingAndRestoresUnreadState<br>PASS SettingsPreviewTests.OlderConnectorExplainsFaceScopeWithoutBreakingChat<br>PASS SettingsPreviewTests.OpacityDragPreviewSurvivesHoverTimerAndCancelsWhenSettingsClose<br>PASS SettingsPreviewTests.PortraitVariantsShareTheSingleFrameQueueAndRepairMissingFiles<br>PASS SettingsPreviewTests.PreviewImagesAndHeaderChangeThroughTheirActualEditors |
| shortcuts | PASS ComposeWorkflowTests.ShortcutOptionsRequireSelectionAndOnlyCreateTheChosenLocation<br>PASS ShortcutTests.RealShellLinksUseTheFixedExecutableAndOriginalIconWithoutWatcherArguments |
| sidebar | PASS SidebarLayoutTests.DefaultSidebarRemainsExpandedAfterApplyingTheWindowPreset<br>PASS SidebarLayoutTests.ShortOverlayScrollsInsteadOfCrushingItsContents<br>PASS SidebarLayoutTests.SidebarSectionsAndHeaderSurviveFontChanges |
| windows-shell | PASS ShortcutTests.RealShellLinksUseTheFixedExecutableAndOriginalIconWithoutWatcherArguments |
| 검토 | PASS ModParkingTests.ParkingRefusesWhenUnsureAndKeepsWhatItCannotRestore |
| 요청-1 | PASS PanelAndLinkTests.ParticipantsOpenAsASidePanel |
| 요청-2 | PASS PanelAndLinkTests.PillFollowsTheThemeAndLooksPressable |
| 요청-3 | PASS PanelAndLinkTests.SecondaryWindowsOpenOnTheOwnersMonitor |
| 요청-4 | PASS PanelAndLinkTests.AutomationNeedsNoPopUpAndKeepsTheNotice |
| 요청-5 | PASS PanelAndLinkTests.ButtonsSitOnTheirParentsColour |
| 요청-6 | PASS PanelAndLinkTests.NarrowWindowCanOpenTheSidebar |
| 요청-7 | PASS PanelAndLinkTests.ConnectionProblemsShowOneActionOnTheNoticeLine |
| 요청-8 | PASS PanelAndLinkTests.ReinstallClosesInstallsAndRelaunches |
| 요청-이전모드 | PASS ModParkingTests.EarlierSetupMovesOutInstallsFreshAndComesBack<br>PASS ModParkingTests.FirstVersionMovesAreCarriedAlong<br>PASS ModParkingTests.KeptSetupProblemsPointToTheButton<br>PASS ModParkingTests.ParkButtonAsksInTheWindow<br>PASS ModParkingTests.ParkingRefusesWhenUnsureAndKeepsWhatItCannotRestore<br>PASS ModParkingTests.RestoreNeverOverwritesAndChecksHashes |

## 측정값 (이 개발 PC에서 측정, 다른 PC 결과 아님)

- 게임 구조 검사(실제 게임 파일 읽기 전용): 어셈블리 69개, 타입 10735개, 367 ms (목표 3,000 ms), 혼용 문제 0건
- 기본 밝게 원본 유지: subtext/window 대비 2.29:1 (4.5 기준 예외)
- 기본 밝게 원본 유지: subtext/panel 대비 3.65:1 (4.5 기준 예외)
- 기본 밝게 원본 유지: notice/pill 대비 3.31:1 (4.5 기준 예외)
- 기본 밝게 원본 유지: warning/window 대비 4.00:1 (4.5 기준 예외)
- scroll bar pictures: 72 (chat, saved rooms, participants, search; 6 themes x light/dark)
- settings window pictures: 84 (6 themes x 2 x 7 categories), no Windows grey pixel
- 3,000개 기록 읽기+묶기: 8 ms (목표 1,500 ms, 화면 그리기 제외), 관리 힙 증가 약 2 MB
- 내 말풍선 테스트(11pt): 실제 글자 폭 39px, 보이는 좌/우 여백 9/9px
- 배치 계산 검사 조합 수: 1296 (너비 2 × 테마 6 × 채팅 모양 3 × 프리셋 4 × 색 위치 3 × 내 캐릭터 3)
- 3,000개 표시(기록 읽기·묶기·배치·첫 그리기): 702 ms (목표 1,500 ms). 검사 프로세스 작업 집합 182 MB, 개인 바이트 132 MB — 검사 실행기 전체 값이라 앱 단독 값이 아님
