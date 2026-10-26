# Copyright 2026 bitHeads, Inc. All Rights Reserved.
class_name BrainCloudS2SGlobalFileV3
extends RefCounted

## S2S Global File V3 operations. Obtain an instance via [method S2SContext.get_global_file_v3].
##
## Every method returns [code]await[/code]-able and also accepts an optional callback
## Callable, e.g. [code]await gfv3.sys_get_file_info(id)[/code] or
## [code]gfv3.sys_get_file_info(id, func(result): ...)[/code].

const SERVICE := "globalFileV3"
const DEFAULT_UPLOAD_PATH := "/s2suploader/globalfile/upload"

var _context: S2SContext

func _init(context: S2SContext) -> void:
	_context = context

func _request(operation: String, data: Dictionary, callback: Callable = Callable()) -> Dictionary:
	return await _context.request({"service": SERVICE, "operation": operation, "data": data}, callback)

# -----------------------------------------------------------------------
# File Info / Query
# -----------------------------------------------------------------------

func sys_get_file_info(file_id: String, callback: Callable = Callable()) -> Dictionary:
	return await _request("SYS_GET_FILE_INFO", {"fileId": file_id}, callback)

func sys_get_file_info_simple(folder_path: String, filename: String, callback: Callable = Callable()) -> Dictionary:
	return await _request("SYS_GET_FILE_INFO_SIMPLE", {"folderPath": folder_path, "filename": filename}, callback)

func sys_check_filename_exists(folder_path: String, filename: String, callback: Callable = Callable()) -> Dictionary:
	return await _request("SYS_CHECK_FILENAME_EXISTS", {"folderPath": folder_path, "filename": filename}, callback)

func sys_check_fullpath_filename_exists(fullpath_filename: String, callback: Callable = Callable()) -> Dictionary:
	return await _request("SYS_CHECK_FULLPATH_FILENAME_EXISTS", {"fullPathFilename": fullpath_filename}, callback)

func sys_get_global_cdn_url(file_id: String, callback: Callable = Callable()) -> Dictionary:
	return await _request("SYS_GET_GLOBAL_CDN_URL", {"fileId": file_id}, callback)

## Pass folder_path="" and recurse=true to list the entire tree.
func sys_get_global_file_list(folder_path: String, recurse: bool, callback: Callable = Callable()) -> Dictionary:
	return await _request("SYS_GET_GLOBAL_FILE_LIST", {"folderPath": folder_path, "recurse": recurse}, callback)

# -----------------------------------------------------------------------
# File Management
# -----------------------------------------------------------------------

func sys_move_to_global_file(user_profile_id: String, user_cloud_path: String, user_cloud_filename: String,
		global_tree_id: String, global_filename: String, overwrite_if_present: bool,
		callback: Callable = Callable()) -> Dictionary:
	return await _request("SYS_MOVE_TO_GLOBAL_FILE", {
		"userProfileId": user_profile_id,
		"userCloudPath": user_cloud_path,
		"userCloudFilename": user_cloud_filename,
		"globalTreeId": global_tree_id,
		"globalFilename": global_filename,
		"overwriteIfPresent": overwrite_if_present,
	}, callback)

## Pass version=-1 for latest / treeVersion=-1 to skip the version check.
func sys_copy_global_file(file_id: String, version: int, new_tree_id: String, tree_version: int,
		new_filename: String, overwrite_if_present: bool, callback: Callable = Callable()) -> Dictionary:
	return await _request("SYS_COPY_GLOBAL_FILE", {
		"fileId": file_id,
		"version": version,
		"newTreeId": new_tree_id,
		"treeVersion": tree_version,
		"newFilename": new_filename,
		"overwriteIfPresent": overwrite_if_present,
	}, callback)

func sys_move_global_file(file_id: String, version: int, new_tree_id: String, tree_version: int,
		new_filename: String, overwrite_if_present: bool, callback: Callable = Callable()) -> Dictionary:
	return await _request("SYS_MOVE_GLOBAL_FILE", {
		"fileId": file_id,
		"version": version,
		"newTreeId": new_tree_id,
		"treeVersion": tree_version,
		"newFilename": new_filename,
		"overwriteIfPresent": overwrite_if_present,
	}, callback)

## Pass version=-1 to delete without a version check.
func sys_delete_global_file(file_id: String, version: int, filename: String, callback: Callable = Callable()) -> Dictionary:
	return await _request("SYS_DELETE_GLOBAL_FILE", {"fileId": file_id, "version": version, "filename": filename}, callback)

func sys_delete_global_files(tree_id: String, folder_path: String, tree_version: int, recurse: bool,
		callback: Callable = Callable()) -> Dictionary:
	return await _request("SYS_DELETE_GLOBAL_FILES", {
		"treeId": tree_id, "folderPath": folder_path, "treeVersion": tree_version, "recurse": recurse,
	}, callback)

# -----------------------------------------------------------------------
# Folder Management
# -----------------------------------------------------------------------

func sys_create_folder(folder_path: String, tree_version: int, folder_name: String, desc: String,
		create_interim_directories: bool, callback: Callable = Callable()) -> Dictionary:
	return await _request("SYS_CREATE_FOLDER", {
		"folderPath": folder_path,
		"treeVersion": tree_version,
		"name": folder_name,
		"desc": desc,
		"createInterimDirectories": create_interim_directories,
	}, callback)

func sys_move_folder(tree_id: String, tree_version: int, new_folder_path: String, updated_name: String,
		create_interim_directories: bool, callback: Callable = Callable()) -> Dictionary:
	return await _request("SYS_MOVE_FOLDER", {
		"treeId": tree_id,
		"treeVersion": tree_version,
		"newFolderPath": new_folder_path,
		"updatedName": updated_name,
		"createInterimDirectories": create_interim_directories,
	}, callback)

func sys_rename_folder(tree_id: String, tree_version: int, updated_name: String, callback: Callable = Callable()) -> Dictionary:
	return await _request("SYS_RENAME_FOLDER", {"treeId": tree_id, "treeVersion": tree_version, "updatedName": updated_name}, callback)

## Resolves the treeId for a folder given its full path (e.g. "/folder/sub/").
func sys_lookup_folder(full_folder_path: String, callback: Callable = Callable()) -> Dictionary:
	return await _request("SYS_LOOKUP_FOLDER", {"fullFolderPath": full_folder_path}, callback)

## Set force=true to also delete any files and sub-folders inside the folder.
func sys_delete_folder(tree_id: String, folder_path: String, tree_version: int, force: bool,
		callback: Callable = Callable()) -> Dictionary:
	return await _request("SYS_DELETE_FOLDER", {
		"treeId": tree_id, "folderPath": folder_path, "treeVersion": tree_version, "force": force,
	}, callback)

# -----------------------------------------------------------------------
# Upload
# -----------------------------------------------------------------------

## Uploads a file to the brainCloud Global File V3 system via S2S.
## Internally performs SYS_PREPARE_UPLOAD to obtain an uploadId, then POSTs the file
## bytes to the upload endpoint as multipart/form-data.
## @param tree_id Folder tree ID ("_root_" for root; use sys_lookup_folder for sub-folders)
## @param filename Name of the file as it will appear in brainCloud
## @param overwrite_if_present Replace any existing file with the same name
## @param file_data File content
func upload_global_file(tree_id: String, filename: String, overwrite_if_present: bool,
		file_data: PackedByteArray, callback: Callable = Callable()) -> Dictionary:
	if _context.get_log_enabled():
		print("[GlobalFileV3] Preparing upload: %s (%d bytes) treeId=%s" % [filename, file_data.size(), tree_id])

	var prepare := await _request("SYS_PREPARE_UPLOAD", {
		"treeId": tree_id,
		"filename": filename,
		"overwriteIfPresent": overwrite_if_present,
		"fileSize": file_data.size(),
	})

	if int(prepare.get("status", 0)) != 200:
		if _context.get_log_enabled():
			print("[GlobalFileV3] SYS_PREPARE_UPLOAD failed: %s" % JSON.stringify(prepare))
		_invoke(callback, prepare)
		return prepare

	var file_details: Dictionary = prepare.get("data", {}).get("fileDetails", {})
	var upload_id: String = String(file_details.get("uploadId", ""))
	if upload_id.is_empty():
		if _context.get_log_enabled():
			print("[GlobalFileV3] SYS_PREPARE_UPLOAD missing fileDetails/uploadId: %s" % JSON.stringify(prepare))
		_invoke(callback, prepare)
		return prepare

	var upload_url := _build_upload_url(file_details, upload_id)

	if _context.get_log_enabled():
		print("[GlobalFileV3] Uploading to: %s" % upload_url)

	var result := await _send_file_upload(upload_url, filename, file_data)
	_invoke(callback, result)
	return result

func _build_upload_url(file_details: Dictionary, upload_id: String) -> String:
	var upload_url: String = String(file_details.get("uploadUrl", ""))
	if not upload_url.is_empty():
		if upload_url.begins_with("http"):
			return upload_url
		# Relative path returned by server — prefix with the context's scheme + host
		return _server_origin() + (upload_url if upload_url.begins_with("/") else "/" + upload_url)
	# Fallback: construct from the context's server url's scheme + host
	return "%s%s?gameId=%s&uploadId=%s" % [
		_server_origin(), DEFAULT_UPLOAD_PATH,
		_context.get_app_id().uri_encode(), upload_id.uri_encode(),
	]

## Unlike the other S2S SDKs, get_server_url() here is the full dispatcher URL
## (scheme + host + path), not just a hostname — so extract "scheme://host[:port]" out of it.
func _server_origin() -> String:
	var url := _context.get_server_url()
	var scheme_end := url.find("://")
	if scheme_end < 0:
		return url
	var path_start := url.find("/", scheme_end + 3)
	if path_start < 0:
		return url
	return url.substr(0, path_start)

func _send_file_upload(upload_url: String, filename: String, file_data: PackedByteArray) -> Dictionary:
	var boundary := "----BrainCloudS2SBoundary%d" % Time.get_ticks_usec()

	var header_part := ("--%s\r\nContent-Disposition: form-data; name=\"file\"; filename=\"%s\"\r\nContent-Type: application/octet-stream\r\n\r\n" % [boundary, filename]).to_utf8_buffer()
	var footer_part := ("\r\n--%s--\r\n" % boundary).to_utf8_buffer()

	var body := PackedByteArray()
	body.append_array(header_part)
	body.append_array(file_data)
	body.append_array(footer_part)

	if _context.get_log_enabled():
		print("[GlobalFileV3] POST %s (%d bytes)" % [upload_url, body.size()])

	var http := HTTPRequest.new()
	_context.add_child(http)

	var headers := PackedStringArray(["Content-Type: multipart/form-data; boundary=%s" % boundary])
	var request_err := http.request_raw(upload_url, headers, HTTPClient.METHOD_POST, body)
	if request_err != OK:
		http.queue_free()
		return {"status": 900, "status_message": "File upload failed to start: error %d" % request_err}

	var completed: Array = await http.request_completed
	var result: int = completed[0]
	var response_body: PackedByteArray = completed[3]
	http.queue_free()

	if result != HTTPRequest.RESULT_SUCCESS:
		return {"status": 900, "status_message": "File upload failed: HTTPRequest result %d" % result}

	var text := response_body.get_string_from_utf8()
	if _context.get_log_enabled():
		print("[GlobalFileV3] Upload response: %s" % text)

	if text.is_empty():
		return {}

	var json := JSON.new()
	if json.parse(text) != OK or typeof(json.get_data()) != TYPE_DICTIONARY:
		return {}
	return json.get_data()

func _invoke(callback: Callable, result: Dictionary) -> void:
	if callback.is_valid():
		callback.call(result)
