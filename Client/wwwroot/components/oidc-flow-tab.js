import "./session-status.js";
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
        this.shadowRoot.innerHTML = `
            <p class="flow-label">Authorization Code + PKCE with BFF SPA Architecture</p>
            <p class="description">
                Initiates an <strong>OIDC login flow</strong> using Authorization Code with PKCE.
                The BFF server handles token exhange on the server side - tokens are never passed directly to the browser.
                The BFF issues a session cookie instead and manages storing and sending access tokens.
            </p>
            <session-status></session-status>
        `;
    }
}

customElements.define('oidc-flow-tab', OidcFlowTab);