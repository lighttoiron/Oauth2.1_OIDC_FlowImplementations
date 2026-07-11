import "./session-status.js";
import "./user-info.js";
import './api-caller.js';
import { loadBaseSheets, loadSheet } from './styles/loader.js';

const baseSheets = await loadBaseSheets();
const ownSheet = await loadSheet('/components/styles/oidc-flow-tab.css');

class OidcFlowTab extends HTMLElement {
    constructor() {
        super();
        this.attachShadow({ mode: 'open' });
        this.shadowRoot.adoptedStyleSheets = [...baseSheets, ownSheet];
    }

    connectedCallback() {
        this._onSessionReady = (e) => {
            const userInfo = this.shadowRoot.querySelector('user-info');
            if (userInfo) {
                userInfo.subjectInfo = e.detail;
            }

            if (this.getAttribute('login-type') === 'full') {
                const apiCaller = this.shadowRoot.querySelector('api-caller');
                if (apiCaller) {
                    apiCaller.sessionReady = true;
                }
            }
        }

        this.addEventListener('session-ready', this._onSessionReady);

        this.shadowRoot.innerHTML = `
            <p class="flow-label">Authorization Code + PKCE with BFF SPA Architecture</p>
            <p class="description">
                Initiates an <strong>OIDC login flow</strong> using Authorization Code with PKCE.
                The BFF server handles token exhange on the server side - tokens are never passed directly to the browser.
                The BFF issues a session cookie instead and manages storing and sending access tokens.
            </p>
            <session-status login-type="${ this.getAttribute('login-type') || 'full' }"></session-status>
            <user-info></user-info>
            <api-caller></api-caller>
        `;
    }

    disconnectedCallback() {
        this.removeEventListener('session-ready', this._onSessionReady);
    }
}

customElements.define('oidc-flow-tab', OidcFlowTab);