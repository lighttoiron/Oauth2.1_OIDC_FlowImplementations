import { loadBaseSheets, loadSheet } from './styles/loader.js';

const baseSheets = await loadBaseSheets();
const ownSheet = await loadSheet('/components/styles/api-caller.css');

// The api=caller element exposes a button that allows a user to attempt to call a protected API
class ApiCaller extends HTMLElement {
    set sessionReady(value) {
        this._sessionReady = value;
        this.render();
    }

    constructor() {
        super();
        this.attachShadow({ mode: 'open'});
        this.shadowRoot.adoptedStyleSheets = [...baseSheets, ownSheet];
    }

    connectedCallback() {
        if (this._sessionReady) {
            this.render();
        }
    }

    disconnectedCallback() {
        const callBtn = this.shadowRoot.getElementById('call-btn');
        if (callBtn) {
            callBtn.removeEventListener('click', this.callApi);
        }
    }

    render() {
        if (!this._sessionReady) return;

        this.shadowRoot.innerHTML = `
            <button class="btn-secondary" id="call-btn">Call Protected API</button>
            <pre id="api-call-result"></pre>
        `;

        this.shadowRoot.getElementById('call-btn')
            .addEventListener('click', this.callApi);
    }

    // Use class arrow function to bind the 'this' context to this function, creating a named lambda that can access the shadow root when called back
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