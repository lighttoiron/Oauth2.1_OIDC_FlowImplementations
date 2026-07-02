import { loadBaseSheets, loadSheet } from './styles/loader.js';

const baseSheets = await loadBaseSheets();
const ownSheet = await loadSheet('/components/styles/api-caller.css');

// The api=caller element exposes a button that allows a user to attempt to call a protected API
class ApiCaller extends HTMLElement {
    constructor() {
        super();
        this.attachShadow({ mode: 'open'});
        this.shadowRoot.adoptedStyleSheets = [...baseSheets, ownSheet];
    }

    connectedCallback() {
        this.shadowRoot.innerHTML = `
            <button class="btn-secondary" id="call-btn">Call Protected API</button>
            <pre id="api-call-result"></pre>
        `;
        this.shadowRoot.getElementById('call-btn')
            .addEventListener('click', () => this.callApi());
    }

    async callApi() {
        const response = await fetch('/bff/protected');
        const pre = this.shadowRoot.getElementById('api-call-result');
        if (!response.ok) {
            pre.textContent = `Error calling protected API: Status was: ${response.status}`;
        }

        const data = await response.json();
        pre.textContent = JSON.stringify(data, null, 2);
    }
}

customElements.define('api-caller', ApiCaller);