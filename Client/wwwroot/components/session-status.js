// We should import these JS files to ensure they are parsed before this file (since we use them as a part of this component)
// For our app, since the user has to interact before they would be rendered, we wouldn't really need this, but it is good practice
// And allows us to not include a script tag in our page HTML for any component not explicitly loaded there
import './sign-in-options.js';
import './api-caller.js';
import { loadBaseSheets, loadSheet } from './styles/loader.js';

const baseSheets = await loadBaseSheets();
const ownSheet = await loadSheet('/components/styles/session-status.css');

// The Session Status element checks our current session status, then displays to the user the result of our sign in attempt
// If the user is signed in, offer a call to the protected API
// If the user is not signed in, offer sign in options
class SessionStatus extends HTMLElement {
    static get observedAttributes() { return ['mode']; };

    attributeChangedCallback() {
        // If we are connected to the document, re-render whenever the mode attribute changes
        if (this.isConnected) this.checkSession();
    }

    constructor() {
        super();
        this.attachShadow({ mode: 'open' });
        this.shadowRoot.adoptedStyleSheets = [...baseSheets, ownSheet];
    }

    connectedCallback() {
        this.shadowRoot.innerHTML = `
            <p class="loading">Loading Session...</p>
        `
        this.checkSession();

        this._onSignedIn = () => this.checkSession();
        this._onSignInError = (e) => {
            this.shadowRoot.innerHTML = `
                <p class="error">Sign in failed: ${e.detail.error}</p>
            `;
        };

        this.addEventListener('signed-in', this._onSignedIn);
        this.addEventListener('sign-in-error', this._onSignInError);
    }

    disconnectedCallback() {
        this.removeEventListener('signed-in', this._onSignedIn);
        this.removeEventListener('sign-in-error', this._onSignInError);
    }

    async checkSession() {
        const response = await fetch('/bff/me');
        const data = await response.json();

        this.shadowRoot.innerHTML = '';

        if (data.authenticated) {
            this.dispatchEvent(new CustomEvent('session-ready', {
                bubbles: true, // Lets objects other than this object receive this event
                composed: true, // Lets listeners that exist outside this element's shadow DOM receive this event
                detail: { subject: data.subject }
            }));
        } else {
            const signInElement = document.createElement('sign-in-options');
            signInElement.setAttribute('login-type', this.getAttribute('login-type' || 'full'));
            this.shadowRoot.appendChild(signInElement);
        }
    }
}

customElements.define('session-status', SessionStatus);