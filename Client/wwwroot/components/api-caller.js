import { loadBaseSheets, loadSheet } from './styles/loader.js';

const baseSheets = await loadBaseSheets();
const ownSheet = await loadSheet('/components/styles/api-caller.css');

// The api-caller element exposes a button that allows a user to attempt to call a protected API
class ApiCaller extends HTMLElement {
    // sessionReady helps us identify if the user is logged in / has a valid session or not
    set sessionReady(value) {
        this._sessionReady = value;
        this.render();
    }

    constructor() {
        super();
        // mode: open allows external page elements to access this shadow root and its contents as if it were in the light DOM.
        // In almost every case mode: open should be used.
        this.attachShadow({ mode: 'open'});
        this.shadowRoot.adoptedStyleSheets = [...baseSheets, ownSheet];
    }

    // connectedCallback is called whenever this element is inserted into a live document
    // It may be called multiple times
    connectedCallback() {
        this.render();
    }

    // disconnectedCallback is the pair of connectedCallback, called whenever this element is removed from a live document (though not on page navigation)
    disconnectedCallback() {
        // Since we registered an event listener to this button, we need to disable it whenever this component is disconnected to prevent double registering.
        const callBtn = this.shadowRoot.getElementById('call-btn');
        if (callBtn) {
            callBtn.removeEventListener('click', this.callApi);
        }
    }

    render() {
        this.shadowRoot.innerHTML = `
        <p class="description">
            Note: If you signed in using the OIDC Login Flow tab the flow will not have included an access token for this API.
            Clear cookies, refresh, and sign in through this tab to access the API.
            </br>
            User has active session with the Auth Server? <span class="subject" style="color:var(${this._sessionReady ? "--color-success" : "--color-error"})">${this._sessionReady ? "true" : "false"}</span>
            </br>
            <button class="btn-secondary" id="call-btn">Call Protected API</button>
        </p>
            <pre id="api-call-result"></pre>
        `;

        this.shadowRoot.getElementById('call-btn')
            .addEventListener('click', this.callApi);
    }

    // Use class arrow function to bind the 'this' context to this function,
    // creating a named lambda that can access the shadow root when called back.
    // We need to name a lambda function because event callbacks are called as plain functions, not class functions,
    //  so we would lose access to this.shadowRoot as 'this' would be undefined.
    callApi = async () => {
        const response = await fetch('/bff/protected');
        const pre = this.shadowRoot.getElementById('api-call-result');
        if (!response.ok) {
            pre.textContent = `Error calling protected API: Status was: ${response.status}`;
            return;
        }

        const data = await response.json();
        pre.textContent = JSON.stringify(data, null, 2);
    }
}

customElements.define('api-caller', ApiCaller);