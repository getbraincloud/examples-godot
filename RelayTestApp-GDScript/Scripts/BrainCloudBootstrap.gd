# Bootstrapper — must be the FIRST autoload in project.godot.
#
# Godot 4.x non-editor runtime does not pre-scan the project for class_name
# declarations. Scripts are compiled on demand in autoload order, so
# BrainCloudWrapper (the SDK autoload) would fail to find its service class
# types unless those scripts are compiled first. Listing them here as preload
# constants forces their compilation — and class_name registration — before
# BrainCloudWrapper.gd is processed.
extends Node

# ── Internal helpers (no class_name dependencies) ─────────────────────────────
const _OperationParam         := preload("res://addons/braincloud/internal/OperationParam.gd")
const _ServiceName            := preload("res://addons/braincloud/internal/ServiceName.gd")
const _ServiceOperation       := preload("res://addons/braincloud/internal/ServiceOperation.gd")
const _RequestState           := preload("res://addons/braincloud/internal/RequestState.gd")
const _ServerCall             := preload("res://addons/braincloud/internal/ServerCall.gd")
const _EndOfBundleMarker      := preload("res://addons/braincloud/internal/EndOfBundleMarker.gd")
const _BrainCloudComms        := preload("res://addons/braincloud/internal/BrainCloudComms.gd")
const _BrainCloudRTTComms     := preload("res://addons/braincloud/internal/BrainCloudRTTComms.gd")
const _BrainCloudRelayComms   := preload("res://addons/braincloud/internal/BrainCloudRelayComms.gd")

# ── Common types ──────────────────────────────────────────────────────────────
const _ACL                    := preload("res://addons/braincloud/common/ACL.gd")
const _AuthenticationIds      := preload("res://addons/braincloud/common/AuthenticationIds.gd")
const _AuthenticationType     := preload("res://addons/braincloud/common/AuthenticationType.gd")
const _GroupACL               := preload("res://addons/braincloud/common/GroupACL.gd")
const _Platform               := preload("res://addons/braincloud/common/Platform.gd")

# ── Codes ─────────────────────────────────────────────────────────────────────
const _StatusCodes            := preload("res://addons/braincloud/StatusCodes.gd")
const _ReasonCodes            := preload("res://addons/braincloud/ReasonCodes.gd")

# ── Core client ───────────────────────────────────────────────────────────────
const _BrainCloudClient       := preload("res://addons/braincloud/BrainCloudClient.gd")

# ── Service classes ───────────────────────────────────────────────────────────
const _AppStore               := preload("res://addons/braincloud/BrainCloudAppStore.gd")
const _AsyncMatch             := preload("res://addons/braincloud/BrainCloudAsyncMatch.gd")
const _Authentication         := preload("res://addons/braincloud/BrainCloudAuthentication.gd")
const _Blockchain             := preload("res://addons/braincloud/BrainCloudBlockchain.gd")
const _Campaign               := preload("res://addons/braincloud/BrainCloudCampaign.gd")
const _Chat                   := preload("res://addons/braincloud/BrainCloudChat.gd")
const _CustomEntity           := preload("res://addons/braincloud/BrainCloudCustomEntity.gd")
const _DataStream             := preload("res://addons/braincloud/BrainCloudDataStream.gd")
const _Entity                 := preload("res://addons/braincloud/BrainCloudEntity.gd")
const _Event                  := preload("res://addons/braincloud/BrainCloudEvent.gd")
const _File                   := preload("res://addons/braincloud/BrainCloudFile.gd")
const _Friend                 := preload("res://addons/braincloud/BrainCloudFriend.gd")
const _Gamification           := preload("res://addons/braincloud/BrainCloudGamification.gd")
const _GlobalApp              := preload("res://addons/braincloud/BrainCloudGlobalApp.gd")
const _GlobalEntity           := preload("res://addons/braincloud/BrainCloudGlobalEntity.gd")
const _GlobalFile             := preload("res://addons/braincloud/BrainCloudGlobalFile.gd")
const _GlobalStatistics       := preload("res://addons/braincloud/BrainCloudGlobalStatistics.gd")
const _Group                  := preload("res://addons/braincloud/BrainCloudGroup.gd")
const _GroupFile              := preload("res://addons/braincloud/BrainCloudGroupFile.gd")
const _Identity               := preload("res://addons/braincloud/BrainCloudIdentity.gd")
const _ItemCatalog            := preload("res://addons/braincloud/BrainCloudItemCatalog.gd")
const _Lobby                  := preload("res://addons/braincloud/BrainCloudLobby.gd")
const _Mail                   := preload("res://addons/braincloud/BrainCloudMail.gd")
const _MatchMaking            := preload("res://addons/braincloud/BrainCloudMatchMaking.gd")
const _Messaging              := preload("res://addons/braincloud/BrainCloudMessaging.gd")
const _OneWayMatch            := preload("res://addons/braincloud/BrainCloudOneWayMatch.gd")
const _PlaybackStream         := preload("res://addons/braincloud/BrainCloudPlaybackStream.gd")
const _PlayerState            := preload("res://addons/braincloud/BrainCloudPlayerState.gd")
const _PlayerStatistics       := preload("res://addons/braincloud/BrainCloudPlayerStatistics.gd")
const _PlayerStatisticsEvent  := preload("res://addons/braincloud/BrainCloudPlayerStatisticsEvent.gd")
const _Presence               := preload("res://addons/braincloud/BrainCloudPresence.gd")
const _Profanity              := preload("res://addons/braincloud/BrainCloudProfanity.gd")
const _PushNotification       := preload("res://addons/braincloud/BrainCloudPushNotification.gd")
const _RTT                    := preload("res://addons/braincloud/BrainCloudRTT.gd")
const _RedemptionCode         := preload("res://addons/braincloud/BrainCloudRedemptionCode.gd")
const _Relay                  := preload("res://addons/braincloud/BrainCloudRelay.gd")
const _Script                 := preload("res://addons/braincloud/BrainCloudScript.gd")
const _SocialLeaderboard      := preload("res://addons/braincloud/BrainCloudSocialLeaderboard.gd")
const _Time                   := preload("res://addons/braincloud/BrainCloudTime.gd")
const _Tournament             := preload("res://addons/braincloud/BrainCloudTournament.gd")
const _UserItems              := preload("res://addons/braincloud/BrainCloudUserItems.gd")
const _VirtualCurrency        := preload("res://addons/braincloud/BrainCloudVirtualCurrency.gd")
