import { loadBaseSheets, loadSheet } from './styles/loader.js';

const baseSheets = await loadBaseSheets();

// The sign-in-options element exposes sign in options to the user.
// The user can choose to sign in with a full page redirect, or through a popup window
class SignInOptions extends HTMLElement {
    constructor() {
        super();
        this.attachShadow({ mode: 'open' });
        this.shadowRoot.adoptedStyleSheets = [...baseSheets];
    }

    connectedCallback() {
        this.shadowRoot.innerHTML = `
            <div class="actions description" style="display:flex; gap:10px; flex-wrap:wrap;">
                <a href="/bff/login?mode=${this.getAttribute('login-type') || 'full'}" class="btn btn-primary">Sign In (Redirect)</a>
                <button id="popup-btn" class="btn btn-secondary">Sign In (Popup)</button>
            </div>
        `;
        this.shadowRoot.getElementById('popup-btn')
            .addEventListener('click', this.signInWithPopup);
    }

    // We need to clean up our broadcastChanngel and popupCloseCheck interval in case this element is cleaned up while the popup window persists.
    disconnectedCallback() {
        if (this._popupChannel) {
            this._popupChannel.close();
            this._popupChannel = null;
        }

        if (this._popupCloseCheck) {
            clearInterval(this._popupCloseCheck);
            this._popupCloseCheck = null;
        }
    }

    signInWithPopup = () => {
        // Open a blank popup window first to get immediate popup load, which can sometimes prevent browsers from closing it as a non-user-initiated popup
        const popup = window.open('about:blank', 'bff_login_popup', 'width=500,height=650');
        const loginType = this.getAttribute('login-type') || 'full';

        // If the popup was closed or didn't open, try a full page redirect login instead
        if (!popup)
        {
            window.location.href = `/bff/login?mode=${loginType}`;
            return;
        }

        // Open a BroadcastChannel to communicate with the popup window
        this._popupChannel = new BroadcastChannel('bff_login');
        let settled = false;

        // Start an interval timer to check if the popup window was closed prematurely, checks every 500ms
        this._popupCloseCheck = setInterval(() => {
            if (popup.closed && !settled) {
                clearInterval(this._popupCloseCheck);
                this._popupChannel.close();
                this.dispatchEvent(new CustomEvent('sign-in-error', {
                    bubbles: true,
                    composed: true,
                    detail: {error: 'Popup closed before sign-in completed.'}
                }));
            }
        }, 500);

        // Set up our listener on the BroadcastChannel so we know when the user has signed in
        this._popupChannel.onmessage = (event) => {
            if (event.data?.type !== 'bff_login_result')
            {
                return;
            }

            // Clean up our popup channel and close check interval since we no longe need them.
            settled = true;
            this._popupChannel.close();
            clearInterval(this._popupCloseCheck);

            if (event.data?.success) {
                this.dispatchEvent(new CustomEvent('signed-in', {
                    bubbles: true,
                    composed: true
                }));
            } else {
                this.dispatchEvent(new CustomEvent('sign-in-error', {
                    bubbles: true,
                    composed: true,
                    detail: {
                        error: event.data?.error
                    }
                }));
            }
        };

        popup.location.href = `/bff/login?popup=true&mode=${loginType}`;
    }
}

customElements.define('sign-in-options', SignInOptions);