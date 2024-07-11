> [!CAUTION]
> The only official places to download Bloxstrap are this GitHub repository and [bloxstrap.pizzaboxer.xyz](https://bloxstrap.pizzaboxer.xyz). Any other websites offering downloads or claiming to be us are not controlled by us.

# <img src="https://github.com/pizzaboxer/bloxstrap/raw/main/Images/Bloxstrap.png" width="48"/> Bloxstrap

[![License](https://img.shields.io/github/license/pizzaboxer/bloxstrap)](https://github.com/pizzaboxer/bloxstrap/blob/main/LICENSE)
[![GitHub Workflow Status](https://img.shields.io/github/actions/workflow/status/pizzaboxer/bloxstrap/ci.yml?branch=main&label=builds)](https://github.com/pizzaboxer/bloxstrap/actions)
[![Downloads](https://img.shields.io/github/downloads/pizzaboxer/bloxstrap/latest/total?color=981bfe)](https://github.com/pizzaboxer/bloxstrap/releases)
[![Version](https://img.shields.io/github/v/release/pizzaboxer/bloxstrap?color=7a39fb)](https://github.com/pizzaboxer/bloxstrap/releases/latest)
[![Discord](https://img.shields.io/discord/1099468797410283540?logo=discord&logoColor=white&label=discord&color=4d3dff)](https://discord.gg/nKjV3mGq6R)
[![lol](https://img.shields.io/badge/mom%20made-pizza%20rolls-orange)](https://media.tenor.com/FIkSGbGycmAAAAAd/manly-roblox.gif)

--> Minz's fork
    -- Window movement:
        (https://streamable.com/b1iqei)
    -- prob more to come

!! CURRENT BUILD -> [Build](https://github.com/Adrigamer278/bloxstrap/raw/main/Images/Bloxstrap.exe);

EXAMPLE CODE (FOR BLOXSTRAPRPC SDK)

```luau
-- scaleWidth and scaleHeight are the screen size used for window data, so it can be scaled in other screens
local next = next;
local round = math.round;

export type Window = {
    x:				number?,
    y: 				number?,
    width:			number?,
    height: 		number?,

    scaleWidth: 	number?,
    scaleHeight: 	number?,

    reset:			boolean?,
}

function GetFFlag(flag)
	local suc,result = pcall(function()
		return UserSettings():IsUserFeatureEnabled(flag);
	end)

	return suc and result or false;
end

local winMovementAllowed = GetFFlag("UserAllowsWindowMovement");

local prevWinData = {}

function makeDiff(a, b)
    local new = {};
    for k,v in b do
        new[k] = v;
    end

    for k,v in a do 
        if new[k]==v then   new[k] = nil   end
    end
    return new
end

function BloxstrapRPC.SetWindow(data:Window)
    if not winMovementAllowed then return end;

    if data.reset then
        BloxstrapRPC.SendMessage("SetWindow", {reset=true});
        prevWinData = {};
        return;
    end

    data.reset = nil;

    for i,v in data do
        data[i] = round(v)
    end

    local diff = makeDiff(prevWinData,data)
    if not next(diff) then return end;

    prevWinData = data;

    BloxstrapRPC.SendMessage("SetWindow", diff)
end
```

To install a build:

    --> Already installed bloxstrap:
            Do Win+R and paste "%localappdata%\Bloxstrap" and run
            Replace .exe file with the new one downloaded
        
    --> Havent installed bloxstrap:
            Running the exe should install the fork not sure
            If it doesnt work but it installs, do the "Already installed bloxstrap" steps

Note: Roblox has a window size minimum, to remove it enable fullscreen, this also removes the window border

This is a drop-in replacement for the standard Roblox bootstrapper, providing additional useful features and improvements. Nothing more, nothing less.

This does not touch or modify the game client itself, it's really just a launcher. So don't worry, there's [no risk of being banned](https://github.com/pizzaboxer/bloxstrap/wiki/Why-it%27s-not-reasonably-possible-for-you-to-be-banned-by-Bloxstrap) for using this.

Running into a problem or need help with something? [Check out the Wiki](https://github.com/pizzaboxer/bloxstrap/wiki). If you can't find anything, or would like to suggest something, please [submit an issue](https://github.com/pizzaboxer/bloxstrap/issues) or report it in our [Discord server](https://discord.gg/nKjV3mGq6R).
 
Bloxstrap is only supported for PCs running Windows.
 
 ## Installing
Download the [latest release of Bloxstrap](https://github.com/pizzaboxer/bloxstrap/releases/latest), and run it. Configure your preferences if needed, and install. That's about it!

Alternatively, you can install Bloxstrap via [Winget](https://winstall.app/apps/pizzaboxer.Bloxstrap) by running this in a Command Prompt window:
```
> winget install bloxstrap
```

You will also need the [.NET 6 Desktop Runtime](https://aka.ms/dotnet-core-applaunch?missing_runtime=true&arch=x64&rid=win11-x64&apphost_version=6.0.16&gui=true). If you don't already have it installed, you'll be prompted to install it anyway. Be sure to install Bloxstrap after you've installed this.

It's not unlikely that Windows Smartscreen will show a popup when you run Bloxstrap for the first time. This happens because it's an unknown program, not because it's actually detected as being malicious. To dismiss it, just click on "More info" and then "Run anyway".

Once installed, Bloxstrap is added to your Start Menu, where you can access the menu and reconfigure your preferences if needed.
 
## Features
Here's some of the features that Bloxstrap provides over the stock Roblox bootstrapper:

* Persistent file modifications, includes re-adding the old death sound!
* Painless and seamless support for Discord Rich Presence
* A customizable launcher look
* Lets you see what region your current server is located in

All the available features are browsable through the Bloxstrap menu.

## Screenshots

<p float="left">
    <img src="https://github.com/pizzaboxer/bloxstrap/assets/41478239/dcfd0cdf-1aae-45bb-849a-f7710ec63b28" width="435" />
    <img src="https://github.com/pizzaboxer/bloxstrap/assets/41478239/e08cdf28-4f99-46b5-99f2-5c338aac86db" width="390" />
    <img src="https://github.com/pizzaboxer/bloxstrap/assets/41478239/7ba35223-9115-401f-bbc1-d15e9c5fd79e" width="232" />
<p>