
using System;
using UnityEngine;

namespace Sortify
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Field, AllowMultiple = false)]
    public class ButtonAttribute : PropertyAttribute
    {
        public string ButtonText;

        /// <summary>
        /// Section title drawn above this button, bold, with a gap above it. Empty for none.
        /// </summary>
        /// <remarks>
        /// Unity's own <see cref="HeaderAttribute"/> cannot do this job. It is declared
        /// <c>AttributeTargets.Field</c>, so it will not even compile on a method — and buttons
        /// are drawn from methods, below every serialized field, so a field header cannot reach
        /// them wherever it is put.
        /// <para>
        /// Set it on the FIRST button of a group; the buttons after it fall under the same title
        /// until another one declares its own.
        /// </para>
        /// </remarks>
        public string Header;

        /// <summary>
        /// Initializes a new instance of the ButtonAttribute with specified button text.
        /// </summary>
        /// <param name="buttonText">The text displayed on the button in the Inspector.</param>
        /// <param name="header">Optional section title drawn above this button.</param>

        public ButtonAttribute(string buttonText = "", string header = "")
        {
            ButtonText = buttonText;
            Header = header;
        }
    }
}

