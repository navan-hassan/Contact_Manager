using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ContactManager.Data;
using ContactManager.Hubs;
using ContactManager.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using MailKit;
using MimeKit;
using MailKit.Net.Smtp;
using ContactManagerStarter.Controllers;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory;

namespace ContactManager.Controllers
{
    public class ContactsController : Controller
    {
        private readonly ApplicationContext _context;
        private readonly IHubContext<ContactHub> _hubContext;
        private readonly ILogger _logger;

        public ContactsController(ApplicationContext context, IHubContext<ContactHub> hubContext, ILoggerFactory logFactory)
        {
            _logger = logFactory.CreateLogger<ContactsController>();
            _context = context;
            _hubContext = hubContext;
        }

        public async Task<IActionResult> DeleteContact(Guid id)
        {
            try
            {
                var contactToDelete = await _context.Contacts
                    .Include(x => x.EmailAddresses)
                    .FirstOrDefaultAsync(x => x.Id == id);
            

                if (contactToDelete == null)
                {
                    _logger.LogError("Could not find contact with id " + id.ToString());
                    return BadRequest();
                }
                _context.EmailAddresses.RemoveRange(contactToDelete.EmailAddresses);
                _context.Contacts.Remove(contactToDelete);
                await _context.SaveChangesAsync();

                await _hubContext.Clients.All.SendAsync("Update");
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError("Could not delete contact with id " + id.ToString());
                return BadRequest();
            }
        }

        public async Task<IActionResult> EditContact(Guid id)
        {
            try
            {
                var contact = await _context.Contacts
                    .Include(x => x.EmailAddresses)
                    .Include(x => x.Addresses)
                    .FirstOrDefaultAsync(x => x.Id == id);

                if (contact == null)
                {
                    _logger.LogError("Contact with id " + id.ToString() + " not found.");
                    return NotFound();
                }

                var viewModel = new EditContactViewModel
                {
                    Id = contact.Id,
                    Title = contact.Title,
                    FirstName = contact.FirstName,
                    LastName = contact.LastName,
                    DOB = contact.DOB,
                    PrimaryEmail = contact.PrimaryEmail,
                    EmailAddresses = contact.EmailAddresses,
                    Addresses = contact.Addresses
                };

                return PartialView("_EditContact", viewModel);
            }
            catch (Exception e)
            {
                _logger.LogError("Could not load contact with id " + id.ToString());
                return NotFound();
            }
        }

        public async Task<IActionResult> GetContacts()
        {
            var contactList = await _context.Contacts
                .Include(x => x.EmailAddresses)
                .OrderBy(x => x.FirstName)
                .ToListAsync();

            return PartialView("_ContactTable", new ContactViewModel { Contacts = contactList });
        }

        public IActionResult Index()
            {
                return View();
            }

        public IActionResult NewContact()
        {
            return PartialView("_EditContact", new EditContactViewModel());
        }

        [HttpPost]
        public async Task<IActionResult> SaveContact([FromBody]SaveContactViewModel model)
        {
            try
            {
                var contact = model.ContactId == Guid.Empty
                    ? new Contact { Title = model.Title, FirstName = model.FirstName, LastName = model.LastName, DOB = model.DOB }
                    : await _context.Contacts.Include(x => x.EmailAddresses).Include(x => x.Addresses).FirstOrDefaultAsync(x => x.Id == model.ContactId);

                if (contact == null)
                {
                    _logger.LogError("Contact with id " + model.ContactId.ToString() + " not found!");
                    return NotFound();
                }

                _context.EmailAddresses.RemoveRange(contact.EmailAddresses);
                _context.Addresses.RemoveRange(contact.Addresses);


                foreach (var email in model.Emails)
                {
                    contact.EmailAddresses.Add(new EmailAddress
                    {
                        Type = email.Type,
                        Email = email.Email,
                        Contact = contact
                    });
                }

                foreach (var address in model.Addresses)
                {
                    contact.Addresses.Add(new Address
                    {
                        Street1 = address.Street1,
                        Street2 = address.Street2,
                        City = address.City,
                        State = address.State,
                        Zip = address.Zip,
                        Type = address.Type
                    });
                }

                contact.Title = model.Title;
                if (model.Emails.Count == 0)
                {
                    contact.PrimaryEmail = null;
                }
                else
                {
                    contact.PrimaryEmail = model.PrimaryEmail;
                }

                contact.FirstName = model.FirstName;
                contact.LastName = model.LastName;
                contact.DOB = model.DOB;

                if (model.ContactId == Guid.Empty)
                {
                    await _context.Contacts.AddAsync(contact);
                    _logger.LogInformation("No exisitng Guid found. Creating new contact.");
                }
                else
                {
                    _logger.LogInformation("Updating contact with id " + model.ContactId.ToString());
                    _context.Contacts.Update(contact);
                }


                await _context.SaveChangesAsync();
                await _hubContext.Clients.All.SendAsync("Update");

                //SendEmailNotification(contact.Id);
                _logger.LogInformation("Successfully saved changes to contact with id " + contact.Id.ToString());
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError("Could not save changes to contact.");
                return NotFound();

            }
        }

        private void SendEmailNotification(Guid contactId)
        {
            var message = new MimeMessage();

            message.From.Add(new MailboxAddress("noreply", "noreply@contactmanager.com"));
            message.To.Add(new MailboxAddress("SysAdmin", "Admin@contactmanager.com"));
            message.Subject = "ContactManager System Alert";

            message.Body = new TextPart("plain")
            {
                Text = "Contact with id:" + contactId.ToString() +" was updated"
            };

            _logger.LogInformation("Contact with id: " + contactId.ToString() +" was updated");



            using (var client = new SmtpClient())
            {
                // For demo-purposes, accept all SSL certificates (in case the server supports STARTTLS)
                client.ServerCertificateValidationCallback = (s, c, h, e) => true;

                client.Connect("127.0.0.1", 25, false);

                client.Send(message);
                client.Disconnect(true);
            }

        }

    }

}