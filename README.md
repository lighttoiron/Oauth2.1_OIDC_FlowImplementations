# Oauth Exploration Lab

This is a simple exploration of **OAuth2.0** and **OpenId Connect** specs to help learn and understand how to practically implement some standard OAuth and OIDC grants.

This lab currently implements:
- The typical **OIDC login flow** (i.e. getting an ID token for a user from a 3rd party sign in website)
- The **Authorization Code with Proof Key for Code Exchange (PKCE)** Oauth2.0 grant
- The **Refresh Token** grant

Additionally this app demonstrates how to set up and integrate with these grants for a **Single Page Application (SPA)** with **Backend For Frontend (BFF)** pattern using **Web Components**, a pattern for creating a highly performant website using patterns that keep secure tokens from ever being stored in the browser.

# Oauth2.0 and OIDC

Code for the Oauth2.0 and OIDC grants exists primarily in the **AuthServer** project.  Program.cs defines the initial setup logic, with each endpoint and other server logic and classes being defined in their own files.

By default, the Auth Server is set to run on https://localhost:7010

# API

Code for the API server and endpoint exists in the **ResourceApi** project.  This is a simple shell that registers itself with the existing Auth Server and then exposes a /protected endpoint that will only return user information if the user calling it has a valid access token provided by the Auth Server.

By default, the API server is set to run on https://localhost:7020

# Client SPA

Code the for client page and BFF server exists in the **Client** project.  This project contains a simple backend server following BFF architecture as well as all of the HTML, CSS, and JavaScript used to build the main page.  Client page HTML and JS/CSS can be found in the wwwroot folder, with index.html containing the base html for the page.

Each web component has its own .js file as well as a .css file following standard industry practices for Web Components.

Note that the BFF server does almost nothing except store secure tokens and expose endpoints that handle any authentication-related calls (either to the Auth Server or the Resource API).

By default, the Client server is set to run on https://localhost:7000

# Using This App

This project is split into three sub-projects: AuthServer, ResourceApi, and Client.  In order to see the whole process in action each of these three projects need to be running.  The Resource API also needs to register with the Auth Server on startup, so the typical process for running this project would be to: 

1. Start AuthServer
2. Start ResourceApi
3. Start Client
4. Access Client by navigating to https://localhost:7000 in your browser.

For convenience I have provided scripts that run all three and set up the project to be debuggable in VS code so you can examine any part of the project as it executes.

## Requirements For Running This Project

In order to run this app you will need:

- .NET 10 SDK
    - Download from [text](https://dotnet.microsoft.com/download)
- Trust the dev certificate by running the following in your terminal:
````code
dotnet dev-certs https --trust
````
- Download this repository from GitHub
- For debugging using my current setup you will also need Visual Studio Code, but you can also just import this project into any IDE you prefer and set up debugging there.

## Running Manually

To run the project manually, open a terminal window in each of the three projects: AuthServer, ResourceApi, and Client.  Then:

1. Start the AuthServer project
2. Start the ResourceApi project
3. Start the Client project
4. Navigate to [text](https://localhost:7000) in your browser

You can start each of the projects by entering the following command into your terminal set at the root of each project (e.g. in the AuthServer folder for the AuthServer project)

````code
dotnet run
````

## Running With 3-in-1 Scripts

I've also provided scripts for running this project on either Windows or Mac.

For Windows:

1. Open a powershell window in the root folder (oauth-lab)
2. Run the following commands: (Set-ExecutionPolicy is used for a one-time permissions setup)
````code
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
.\run-all.ps1
````

For Mac:

1. Open a terminal window in the root folder (oauth-lab)
2. Run the following commands: (chmod grants permission for run-all.sh to execute)
````code
chmod +x run-all.sh
./run-all.sh
````

3. After the script finishes, navigate to [text](https://localhost:7000)

## Running With Debugging

If you want to debug the backend server behavior, you can use Visual Studio Code to run these projects (or you can import this project into any IDE of your choice).

To run in VS code with debugging:

1. Open the project in VS Code
2. Open the Run and Debug window (the play button with a bug icon on the far left hand side of the IDE)
3. Select **All Projects** from the dropdown menu next to the play button on the top left of the IDE (or select any single project like AuthServer to just run one)
4. Press the play button (the triangle next to RUN AND DEBUG near the dropdown you just used)

This will open three windows in your browser (one each to localhost:7000, localhost:7010, and localhost:7020).  The tab titled **OAuth Lab Client Page (BFF with Web Components)** is the only one you need to interact with manually from the browser.  You will now be able to set breakpoints and watch the code as it executes from the server side.  To debug the web page itself, you will need to use your browser's built-in developer tools.

By default, each project is set to use Http Logging, showing calls made and received in the terminal window the project is running in.

## Ineracting With The Web Page

Once you have the app running you will be presented with a web page with three tabs: **Login + API**, **Pure OIDC Login Flow**, and **Dump Server Info**

### Login + API

This page presents you with the complete Authorization Code + PKCE with Login grant, incorporating OAuth2.0 and OIDC flows to allow the user to simultaneously log in and provide consent for the Client app to access the protected resource API.  This grant will return both an ID token and an access token for the user if they log in and grant consent, but also allows them to deny consent and return only the ID token.  The Refresh Token grant is also a part of this flow, so the user will also receive a refresh token.

This tab allows the user to sign in, either via full page redirect (which is often industry standard since popup windows may be blocked) or via a popup window (which is less intrusive to the user, especially on a SPA webpage).  Both buttons will allow the user to sign in (valid credentials are pre-populated) and, if the user signs in, to grant consent for the Client app to access the protected resource API.

After successfully signing in, the page will now display your username, if you have API access, and if you have received a refresh token.  You can also access the protected resource API via the Call Protected API button, which will display the returned JSON in the box at the bottom of the page.

### Pure OIDC Login Flow

This tab looks much the same as the Login + API tab, but it will perform an OIDC login flow only and will not prompt the user to grant permission to access the protected API or return any access tokens.  Use this tab to examine the OIDC login flow and ID token grant without any of the access or refresh tokens.

### Dump Server Info

This tab allows easy access to the internal storage on the Auth Server, allowing you to easily see current user sessions, issued refresh tokens, etc.  This is mostly used for easy debugging so you don't always need to run the server through a debugger when looking at current flows.

You can also use the Clear All Info button to clear all the stores (except registered Username/Password combinations) on the BFF server and the Auth server, effectively signing out all accounts.

### Cookies And Resetting

In a typical login and access token flow, cookies are uesd to store user session information.  For this lab, actual user information (like ID and access tokens) is never stored in cookies, but opaque session cookies are uesd to look up that user information in the Auth Server and BFF server.

To fully reset the lab to 0, you will need to both clear the server cached information (using the Clear All Info button or closing and restarting each of the three projects in the terminal) and also clear the cookies (the Clear All Info button will also attempt to clear any stored cookies).  If you are trying to start over but find yourself still signed in, there are probably still lingering session cookies or session information in the server stores.