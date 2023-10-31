# Godot Authentication

This example demonstrates how to get brainCloud up and running in Godot, as well as how to use various brainCloud services, including: [Authentication](https://getbraincloud.com/apidocs/apiref/?csharp#capi-authentication), [Identity](https://getbraincloud.com/apidocs/apiref/?csharp#capi-identity), and [Entity](https://getbraincloud.com/apidocs/apiref/?csharp#capi-entity) (with more coming soon).

It was made with Godot 4, and is based off of the [Unity Authentication example](https://github.com/getbraincloud/examples-unity/tree/master/Authentication) that also uses the [brainCloud C# client library](https://github.com/getbraincloud/braincloud-csharp).

## BCManager

This project uses a script called **BCManager.cs** to define and create brainCloud requests. It has been added to the project's autoload list to act as a singleton. More information on Godot's Autoload feature can be found in their [docs](https://docs.godotengine.org/en/stable/tutorials/scripting/singletons_autoload.html).

## Getting Started

1. Create a new app in the [brainCloud server portal](https://portal.braincloudservers.com/) and take note of the application IDs at Design > Core App Info > Application IDs
2. Open the project in the Godot editor
    - In the **BCManager.cs** script, replace the `_secretKey` and `_appId` values with the ones from the newly created app
    - When building the app in the Godot Editor, there may be "duplicate attribute" errors in the **AssemblyInfo.cs** file. If this happens, comment out, or delete the duplicated lines that have been highlighted as errors, and the project should build successfully. 
3. The project should now be ready to run. Ensure that **main.tscn** has been set as the main scene, and then click **Run Project** to explore brainCloud.

---

For more information on brainCloud and its services, please check out the [brainCloud Docs](https://getbraincloud.com/apidocs/) and [API Reference](https://getbraincloud.com/apidocs/apiref/?csharp#introduction).
