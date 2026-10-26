# Copyright 2026 bitHeads, Inc. All Rights Reserved.
class_name BrainCloudS2SPRL
extends RefCounted

## Handles the Pre-Ready Launch (PRL) flow for custom servers launched by brainCloud.
##
## When the PRE_READY_LAUNCH environment variable is "true", the server must wait for
## its assigned lobby to reach the "starting" state before proceeding with launch:
## [codeblock]
## if BrainCloudS2SPRL.is_pre_ready_launch():
##     var lobby_id := BrainCloudS2SPRL.parse_server_context().get("lobbyId", "")
##     # ... await the lobby "starting" event via RTT, then proceed ...
## [/codeblock]
##
## Every function takes an optional override parameter that bypasses the environment
## variable lookup — this keeps the functions unit-testable without forking a process
## with different environment variables.

## Returns true if the PRE_READY_LAUNCH environment variable is "true" (case-insensitive).
static func is_pre_ready_launch(override: String = "") -> bool:
	var val := override if not override.is_empty() else OS.get_environment("PRE_READY_LAUNCH")
	return val.to_lower() == "true"

## Returns the timeout in seconds from PRL_TIMEOUT_SECS or PRE_READY_LAUNCH_TIMEOUT_SECS.
## Defaults to 60 if neither is set (or not a valid integer).
static func get_timeout_secs(override_secs: int = -1) -> int:
	if override_secs >= 0:
		return override_secs

	var val := OS.get_environment("PRL_TIMEOUT_SECS")
	if val.is_valid_int():
		return val.to_int()

	val = OS.get_environment("PRE_READY_LAUNCH_TIMEOUT_SECS")
	if val.is_valid_int():
		return val.to_int()

	return 60

## Parses the SERVER_CONTEXT environment variable into a Dictionary.
## Tolerates single-quoted and backslash-escaped JSON strings. Returns {} on any
## parse failure (including an unset/empty environment variable).
static func parse_server_context(override: String = "") -> Dictionary:
	var val := override if not override.is_empty() else OS.get_environment("SERVER_CONTEXT")
	if val.is_empty():
		val = "{}"

	val = val.strip_edges()
	if val.length() >= 2 and val.begins_with("'") and val.ends_with("'"):
		val = val.substr(1, val.length() - 2)
	val = val.replace("\\\"", "\"")

	var json := JSON.new()
	if json.parse(val) != OK or typeof(json.get_data()) != TYPE_DICTIONARY:
		return {}
	return json.get_data()
