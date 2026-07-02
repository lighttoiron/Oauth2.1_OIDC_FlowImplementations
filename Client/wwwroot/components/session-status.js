// We should import these JS files to ensure they are parsed before this file (since we use them as a part of this component)
// For our app, since the user has to interact before they would be rendered, we wouldn't really need this, but it is good practice
// And allows us to not include a script tag in our page HTML for any component not explicitly loaded there
import './sign-in-options.js';
import './api-caller.js';

// The Session Status element checks our current session status, then displays to the user the result of our sign in attempt
// If the user is signed in, offer a call to the protected API
// If the user is not signed in, offer sign in options
class SessionStatus extends HTMLElement {
    constructor() {
        super();
        this.attachShadow({ mode: 'open' });
    }

    connectedCallback() {
        this.shadowRoot.innerHTML = `
            <style>
                :host { display: block; }
                .loading {
                    font-family: 'JetBrains Mono', monospace;
                    font-size: 12px;
                    color: #4A4D5E;
                }
                .signed-in-bar {
                    display: flex;
                    align-items: center;
                    justify-content: space-between;
                    padding: 10px 14px;
                    background: #1A1D27;
                    border: 1px solid #2A2D3A;
                    border-radius: 6px;
                    margin-bottom: 20px;
                }
                .signed-in-bar .subject {
                    font-family: 'JetBrains Mono', monospace;
                    font-size: 12px;
                    color #30D158;
                }
                .signed-in-bar .indicator {
                    width: 7px;
                    height: 7px;
                    background: #30D158;
                    border-radius: 50%;
                    margin-right: 8px;
                    display: inline-block;
                }
                .error {
                    font-family: 'JetBrains Mono', monospace;
                    font-size: 12px;
                    color: #FF453A;
                    padding: 10px 14px;
                    background: rgba(255, 69, 58, 0.08);
                    border: 1px solid rgba(255, 69, 58, 0.2);
                    border-radius: 6px;
                }
            </style>
            <p>Loading Session...</p>
        `
        this.checkSession();

        this.addEventListener('signed-in', () => this.checkSession());
        this.addEventListener('sign-in-error', (event) => {
            this.shadowRoot.innerHTML = `
                <p class="error">Sign in failed: ${event.detail.error}</p>
            `;
        });
    }

    async checkSession() {
        const response = await fetch('/bff/me');
        const data = await response.json();

        this.shadowRoot.innerHTML = '';

        if (data.authenticated) {
            const bar = document.createElement('div');
            bar.innerHTML = `
                <span>
                    <span class="indicator"></span>
                    <span class="subject">${data.subject}</span>
                </span>
            `;
            this.shadowRoot.appendChild(bar);
            this.shadowRoot.appendChild(document.createElement('api-caller'));
        } else {
            this.shadowRoot.appendChild(document.createElement('sign-in-options'));
        }
    }
}

customElements.define('session-status', SessionStatus);