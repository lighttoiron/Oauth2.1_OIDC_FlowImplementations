import "./session-status.js";

class OidcFlowTab extends HTMLElement {
    constructor() {
        super();
        this.attachShadow({ mode: 'open' });
    }

    connectedCallback() {
        this.shadowRoot.innerHTML = `
            <style>
                :host {
                    display: block;
                    padding 32px 0;
                }
                .description {
                    font-family: 'Inter', sans-serif;
                    font-size: 13px;
                    color: #8E8EA0;
                    line-height: 1.6;
                    margin: 0 0 28px;
                    padding-bottom: 20px;
                    border-bottom: 1px solid #2A2D3A;
                }
                .description strong {
                    color: #C0C0D8;
                    font-weight: 500;
                }
                .flow-label {
                    font-family: 'JetBrains Mono', monospace;
                    font-size: 10px;
                    letter-spacing: 0.12em;
                    text-transform: uppercase;
                    color: #7C6AFE;
                    margin: 0 0 16px;
                }
            </style>
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