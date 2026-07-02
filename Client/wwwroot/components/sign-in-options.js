// The sign-in-options element exposes sign in options to the user.
// The user can choose to sign in with a full page redirect, or through a popup window
class SignInOptions extends HTMLElement {
    constructor() {
        super();
        this.attachShadow({ mode: 'open' });
    }

    connectedCallback() {
        this.shadowRoot.innerHTML = `
            <style>
                :host { display: block; }
                .actions {
                    display: flex;
                    gap: 10px;
                    flex-wrap: wrap;
                }
                button, a {
                    font-family: 'Inter', sans-serif;
                    font-size: 13px;
                    font-weight: 500;
                    padding: 9px 18px;
                    border-radius: 6px;
                    cursor: pointer;
                    transition: all 0.15s;
                    text-decoration: none;
                    display: inline-block;
                }
                .btn-redirect {
                    background: #7C6AFE;
                    color: #fff;
                    border: 1px solid #7C6AFE
                }
                .btn-redirect:hover {
                    background: #8F7FFF;
                    border-color: #8F7FFF;
                }
                .btn-popup {
                    background: transparent;
                    color: #C0C0D8;
                    border: 1px solid 2A2D3A;
                }
                .btn-popup:hover {
                    border-color: #7C6AFE;
                    color: #E0E0F0;
                }
            </style>
            <div class="actions">
                <a href="/bff/login" class="btn-redirect">Sign In (Redirect)</a>
                <button id="popup-btn" class="btn-popup">Sign In (Popup)</button>
            </div>
        `;
        this.shadowRoot.getElementById('popup-btn')
            .addEventListener('click', () => this.signInWithPopup());
    }

    signInWithPopup() {
        const popup = window.open('about:blank', 'bff_login_popup', 'width=500,height=650');

        if (!popup)
        {
            window.location.href = '/bff/login'; // If the popup window was blocked, perform a login by redirect instead
            return;
        }

        const channel = new BroadcastChannel('bff_login');
        let settled = false;

        const closeCheck = setInterval(() => {
            if (popup.closed && !settled) {
                clearInterval(closeCheck);
                channel.close();
                this.dispatchEvent(new CustomEvent('sign-in-error', {
                    bubbles: true, // Lets objects other than this object receive this event
                    composed: true, // Lets listeners that exist outside this element's shadow DOM receive this event
                    detail: {error: 'Popup closed before sign-in completed.'}
                }));
            }
        }, 500);

        channel.onmessage = (event) => {
            if (event.data?.type !== 'bff_login_result')
            {
                return;
            }

            settled = true;
            channel.close();
            clearInterval(closeCheck);

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

        popup.location.href = '/bff/login?popup=true';
    }
}

customElements.define('sign-in-options', SignInOptions);