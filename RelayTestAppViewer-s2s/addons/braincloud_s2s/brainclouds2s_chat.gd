# Copyright 2026 bitHeads, Inc. All Rights Reserved.
class_name BrainCloudS2SChat
extends RefCounted

## S2S chat channel operations. Obtain an instance via [method S2SContext.get_chat_service].
##
## The S2S "SYS_" chat operations are a distinct, more permissive counterpart of the
## client-facing CHANNEL_CONNECT/CHANNEL_DISCONNECT: server-side (ChatService.java)
## routes SYS_CHANNEL_CONNECT with enforceMembership=false, so an S2S context can
## subscribe to a lobby's system channel ("<appId>:sy:_lobby_<instanceId>", see
## [method build_lobby_system_channel_id]) without being a member of that lobby — the
## plain (non-SYS) operation is player-only and will error for an S2S caller.
##
## Once connected, incoming messages arrive via RTT as
## {"service":"chat","operation":"INCOMING","data":{"content":{...message...}}} —
## register a raw RTT callback (S2SContext.get_rtt_service().register_raw_callback)
## to receive them; RTT must already be enabled (S2SContext.enable_rtt) first.

const SERVICE := "chat"

var _context: S2SContext

func _init(context: S2SContext) -> void:
	_context = context

## Subscribes to a channel and returns its recent message history (up to max_return).
## Response shape: {"data": {"messages": [{"date", "msgId", "from": {"name","pic","id"},
## "content": {"text","custom"}, "chId", ...}]}, "status": 200}.
func sys_channel_connect(channel_id: String, max_return: int = 20, callback: Callable = Callable()) -> Dictionary:
	return await _context.request({
		"service": SERVICE,
		"operation": "SYS_CHANNEL_CONNECT",
		"data": {"channelId": channel_id, "maxReturn": max_return},
	}, callback)

func sys_channel_disconnect(channel_id: String, callback: Callable = Callable()) -> Dictionary:
	return await _context.request({
		"service": SERVICE,
		"operation": "SYS_CHANNEL_DISCONNECT",
		"data": {"channelId": channel_id},
	}, callback)

## Resolves a channel id from a type + sub-id (e.g. ("gl", "gl") for the app's global
## channel — see cpp-examples/relaytestapp/src/globalChat.cpp:25,69-70 for that exact
## convention). channel_type is "gl" (global), "gr" (group), or "dy" (dynamic).
## Requires RTT to already be enabled (RTT_NOT_ENABLED, error 40601, otherwise).
## Response: {"data": {"channelId": "<appId>:<type>:<resolved>"}, "status": 200}.
func get_channel_id(channel_type: String, channel_sub_id: String, callback: Callable = Callable()) -> Dictionary:
	return await _context.request({
		"service": SERVICE,
		"operation": "GET_CHANNEL_ID",
		"data": {"channelType": channel_type, "channelSubId": channel_sub_id},
	}, callback)

## Builds a lobby's system channel id from its lobby id. Lobby ids look like
## "<appId>:<lobbyType>:<instanceNum>" — everything after the first ":" is the
## "instance id" the channel id is built from. Mirrors cpp-s2s's
## BrainCloudS2SPRL::buildChannelId() (brainclouds2s-prl.cpp:272-281), confirmed
## against the live server.
func build_lobby_system_channel_id(lobby_id: String) -> String:
	var instance_id := lobby_id
	var colon := lobby_id.find(":")
	if colon >= 0:
		instance_id = lobby_id.substr(colon + 1)
	return "%s:sy:_lobby_%s" % [_context.get_app_id(), instance_id]
