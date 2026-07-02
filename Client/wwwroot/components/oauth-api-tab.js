class OauthApiTab extends HTMLElement {
    constructor() {
        super();
        this.attachShadow({ mode: 'open' });
    }

    connectedCallback() {
        this.shadowRoot.innerHTML = `
            <style>
                :host {
                    display: block;
                    padding: 32px 0;
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
                .placeholder {
                    font-family: 'JetBrains Mono', monospace;
                    font-size: 12px;
                    color: #4A4D5E;
                    padding: 32px;
                    border: 1px dashed #2A2D3A;
                    border-radius: 6px;
                    text-align: center;
                }
            </style>
            <p class="flow-label">Bearer Token - Resource Server</p>
            <p class="description">
                <strong>Accesses a Protected Resource</strong> from an external API using OAuth2.0 Bearer access tokens.
                The BFF server attaches the token server-side before forwarding the request to the protected API.
                Token is retrieved using the Oauth2.0 Authorization Code flow with PKCE using a BFF SPA architecture.
            </p>
        `;
    }
}

customElements.define('oauth-api-tab', OauthApiTab);